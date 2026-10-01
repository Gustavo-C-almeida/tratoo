using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tratoo.Domain.Data;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Modo migração: <c>dotnet Tratoo.API.dll --migrate-only</c> aplica as migrations do
    /// <see cref="TratooContext"/>, roda o <see cref="VectorDbInitializer"/> e encerra com
    /// 0 (sucesso) ou ≠ 0 (falha). Usado pelo serviço <c>migrate</c> do compose e, quando
    /// ativado, pelo <c>preDeployCommand</c> da Railway (ver railway.toml).
    ///
    /// NÃO existe host aqui: os DbContexts são montados à mão, sem WebApplication, sem DI.
    /// Por construção não há Kestrel nem BackgroundServices — impossível este processo
    /// expirar contrato ou liberar pagamento enquanto migra.
    ///
    /// Corrida entre instâncias: o <c>MigrateAsync</c> do EF Core 9 + Npgsql 9 adquire
    /// <c>LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE</c> dentro de uma
    /// transação. Dois processos simultâneos se serializam; o segundo encontra tudo
    /// aplicado. Por ser trava de transação (não advisory de sessão), funciona inclusive
    /// pelo PgBouncer em modo transaction do Neon. Ver Docs/TRILHA2-DECISOES.md, seção 5.
    /// </summary>
    public static class ModoMigracao
    {
        public const string Argumento = "--migrate-only";

        /// <summary>
        /// Connection string opcional só para a migração — em produção, o endpoint DIRETO
        /// do Neon (sem "-pooler"), que é o que o Neon recomenda para mudança de schema.
        /// Ausente, usa a DefaultConnection.
        /// </summary>
        public const string ConexaoMigracao = "MigrationConnection";

        public const int ExitSucesso = 0;
        public const int ExitFalha = 1;
        public const int ExitConfiguracaoAusente = 2;

        public static bool FoiSolicitado(string[] args) =>
            args.Any(a => string.Equals(a, Argumento, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Connection string usada para as migrations do TratooContext e de onde ela veio.
        /// <c>null</c> se nenhuma das duas estiver configurada.
        /// </summary>
        public static (string Conexao, string Origem)? ResolverConexaoPrincipal(IConfiguration configuration)
        {
            var migracao = configuration.GetConnectionString(ConexaoMigracao);
            if (!string.IsNullOrWhiteSpace(migracao))
                return (migracao, ConexaoMigracao);

            var padrao = configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(padrao))
                return (padrao, "DefaultConnection");

            return null;
        }

        public static async Task<int> ExecutarAsync()
        {
            // Mesmas fontes que o host web usa para estes valores: JSON do diretório de
            // trabalho (content root) + variáveis de ambiente, que vencem.
            var ambiente = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production;
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{ambiente}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            using var loggerFactory = LoggerFactory.Create(b => b
                .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
                .AddFilter("Microsoft", LogLevel.Warning)
                // Em banco vazio o EF consulta "__EFMigrationsHistory" antes de criá-la e loga
                // "fail: Failed executing DbCommand" — esperado, mas parece erro no log do
                // deploy. Falha real não se perde: vira exceção e é logada no catch abaixo.
                .AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None)
                // "Applying migration '...'" / "No migrations were applied" vêm daqui.
                .AddFilter("Microsoft.EntityFrameworkCore.Migrations", LogLevel.Information));
            var logger = loggerFactory.CreateLogger("Tratoo.Migracao");

            var principal = ResolverConexaoPrincipal(configuration);
            var vetorial = configuration.GetConnectionString("VectorConnection");
            if (principal is null || string.IsNullOrWhiteSpace(vetorial))
            {
                logger.LogError(
                    "Migração abortada: configure ConnectionStrings:DefaultConnection (ou {Migracao}) e ConnectionStrings:VectorConnection.",
                    ConexaoMigracao);
                return ExitConfiguracaoAusente;
            }

            try
            {
                var (conexao, origem) = principal.Value;
                logger.LogInformation("Migrations do TratooContext em {Alvo} (via {Origem}).", Descrever(conexao), origem);
                if (UsaPooler(conexao))
                    logger.LogWarning(
                        "Migração pelo endpoint -pooler do Neon. Funciona (a trava do EF é transacional), mas o Neon recomenda a conexão direta: defina ConnectionStrings:{Migracao}.",
                        ConexaoMigracao);

                var opcoes = new DbContextOptionsBuilder<TratooContext>()
                    .UseNpgsql(conexao)
                    .UseLoggerFactory(loggerFactory)
                    .Options;

                await using (var db = new TratooContext(opcoes))
                {
                    var pendentes = (await db.Database.GetPendingMigrationsAsync()).ToList();
                    logger.LogInformation("{Quantidade} migration(s) pendente(s){Lista}.",
                        pendentes.Count, pendentes.Count > 0 ? ": " + string.Join(", ", pendentes) : "");

                    // Adquire a trava, aplica o que faltar (cada migration em transação) e
                    // falha se o modelo tiver mudanças sem migration (regra do EF Core 9).
                    await db.Database.MigrateAsync();
                }

                logger.LogInformation("Schema vetorial em {Alvo}.", Descrever(vetorial));
                var opcoesVetor = new DbContextOptionsBuilder<VectorContext>()
                    .UseNpgsql(vetorial, o => o.UseVector())
                    .UseLoggerFactory(loggerFactory)
                    .Options;

                await using (var vetorDb = new VectorContext(opcoesVetor))
                {
                    await new VectorDbInitializer(vetorDb, loggerFactory.CreateLogger<VectorDbInitializer>())
                        .InitializeAsync();
                }

                logger.LogInformation("Migração concluída.");
                return ExitSucesso;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Migração falhou. Nada foi servido; o deploy/compose não deve prosseguir.");
                return ExitFalha;
            }
        }

        /// <summary>Host/porta/banco, nunca usuário nem senha.</summary>
        private static string Descrever(string conexao)
        {
            try
            {
                var b = new NpgsqlConnectionStringBuilder(conexao);
                return $"{b.Host}:{b.Port}/{b.Database}";
            }
            catch
            {
                return "(connection string ilegível)";
            }
        }

        private static bool UsaPooler(string conexao)
        {
            try { return new NpgsqlConnectionStringBuilder(conexao).Host?.Contains("-pooler", StringComparison.OrdinalIgnoreCase) == true; }
            catch { return false; }
        }
    }
}
