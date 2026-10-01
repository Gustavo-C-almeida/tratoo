using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// <c>--migrate-only</c>. A migração bem-sucedida precisa de Postgres de verdade e é
    /// validada no compose (banco vazio, 2×, concorrente). Aqui fica o que é
    /// determinístico sem banco: a escolha da connection string e — rodando o
    /// PROCESSO real — que o modo nunca cai no host web.
    /// </summary>
    public class ModoMigracaoTests
    {
        private static IConfiguration Config(params (string Chave, string? Valor)[] pares) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(pares.ToDictionary(p => p.Chave, p => p.Valor))
                .Build();

        [Theory]
        [InlineData("--migrate-only")]
        [InlineData("--MIGRATE-ONLY")]
        public void Reconhece_o_argumento(string arg)
        {
            Assert.True(ModoMigracao.FoiSolicitado(new[] { arg }));
            Assert.False(ModoMigracao.FoiSolicitado(new[] { "--healthcheck" }));
            Assert.False(ModoMigracao.FoiSolicitado(Array.Empty<string>()));
        }

        [Fact]
        public void Prefere_a_conexao_de_migracao_quando_existe()
        {
            var r = ModoMigracao.ResolverConexaoPrincipal(Config(
                ("ConnectionStrings:MigrationConnection", "Host=direto"),
                ("ConnectionStrings:DefaultConnection", "Host=pooler")));

            Assert.Equal(("Host=direto", "MigrationConnection"), r);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Sem_conexao_de_migracao_usa_a_DefaultConnection(string? migracao)
        {
            var r = ModoMigracao.ResolverConexaoPrincipal(Config(
                ("ConnectionStrings:MigrationConnection", migracao),
                ("ConnectionStrings:DefaultConnection", "Host=pooler")));

            Assert.Equal(("Host=pooler", "DefaultConnection"), r);
        }

        [Fact]
        public void Sem_nenhuma_conexao_retorna_null()
        {
            Assert.Null(ModoMigracao.ResolverConexaoPrincipal(Config()));
        }

        private static int PortaLivre()
        {
            using var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }

        private static async Task<(int ExitCode, string Saida)> RodarApiAsync(
            string argumento, IDictionary<string, string?> ambiente)
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "Tratoo.API.dll");
            Assert.True(File.Exists(dll), $"Tratoo.API.dll não encontrado em {AppContext.BaseDirectory}");

            // Diretório de trabalho VAZIO: o appsettings.json copiado para a pasta dos
            // testes aponta para o Neon de produção e não pode ser lido por este teste.
            var cwd = Directory.CreateTempSubdirectory("tratoo-migrate-teste-").FullName;

            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add(argumento);

            foreach (var chave in psi.Environment.Keys.Where(k => k.StartsWith("ConnectionStrings__")).ToList())
                psi.Environment.Remove(chave);
            foreach (var (k, v) in ambiente)
                psi.Environment[k] = v;

            using var processo = Process.Start(psi)!;
            var stdout = processo.StandardOutput.ReadToEndAsync();
            var stderr = processo.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await processo.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                processo.Kill(entireProcessTree: true);
                throw new TimeoutException($"{argumento} não encerrou em 90s (subiu um host?).");
            }
            finally
            {
                Directory.Delete(cwd, recursive: true);
            }

            return (processo.ExitCode, await stdout + await stderr);
        }

        [Fact]
        public async Task Banco_fora_do_ar_encerra_com_falha_sem_subir_Kestrel_nem_BackgroundServices()
        {
            // Porta local sem ninguém escutando = "banco fora do ar", recusa imediata.
            var portaBanco = PortaLivre();
            var portaHttp = PortaLivre();
            var conexao = $"Host=127.0.0.1;Port={portaBanco};Database=x;Username=x;Password=x;Timeout=3";

            var (exit, saida) = await RodarApiAsync(ModoMigracao.Argumento, new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["PORT"] = portaHttp.ToString(),
                ["ConnectionStrings__DefaultConnection"] = conexao,
                ["ConnectionStrings__VectorConnection"] = conexao
            });

            // Exatamente 1 e a mensagem do modo: se o argumento caísse no host web, o
            // processo morreria por exceção não tratada no VectorDbInitializer — outro
            // exit code e sem esta mensagem.
            Assert.Equal(ModoMigracao.ExitFalha, exit);
            Assert.Contains("Migração falhou", saida);
            Assert.Contains($"127.0.0.1:{portaBanco}/x", saida);

            Assert.DoesNotContain("Now listening", saida);
            Assert.DoesNotContain("Service iniciado", saida); // "PagamentoLiberacaoService iniciado." etc.
        }

        [Fact]
        public async Task Sem_connection_string_encerra_com_codigo_proprio()
        {
            var (exit, saida) = await RodarApiAsync(ModoMigracao.Argumento, new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production"
            });

            Assert.Equal(ModoMigracao.ExitConfiguracaoAusente, exit);
            Assert.Contains("Migração abortada", saida);
        }
    }
}
