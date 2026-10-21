using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Features.Shared;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Rotação da chave de dados: <c>dotnet Tratoo.API.dll --recifrar-dados [--aplicar]</c>
    /// regrava com a chave ATUAL (<c>DataProtector:Chave</c>) todo valor que ainda está
    /// cifrado com a ANTERIOR (<c>DataProtector:ChaveAnterior</c>). Roteiro completo em
    /// Docs/ROTACAO-CHAVE-DADOS.md.
    ///
    /// Sem <c>--aplicar</c> é só um relatório: classifica cada valor das colunas cifradas e
    /// não grava nada. Com <c>--aplicar</c>, recifra numa única transação e confere o
    /// resultado relendo o banco.
    ///
    /// Garantias:
    ///  • nada decifrado é registrado — a saída tem só nomes de coluna e contagens;
    ///  • só é regravado o valor que decifra com a anterior e NÃO com a atual; valores que
    ///    decifram com as duas (praticamente impossível) ou com nenhuma são contados e
    ///    deixados como estão;
    ///  • cada valor novo é conferido (decifra com a atual para o mesmo texto) antes de ir
    ///    para o banco;
    ///  • a gravação só acontece se o valor no banco ainda for o lido (a API pode estar
    ///    gravando ao mesmo tempo); se algum mudou, a transação inteira é desfeita;
    ///  • é idempotente: rodar de novo depois de concluído não altera nada.
    ///
    /// Como no <see cref="ModoMigracao"/>, não existe host: sem Kestrel e sem
    /// BackgroundServices.
    /// </summary>
    public static class ModoRecifragem
    {
        public const string Argumento = "--recifrar-dados";
        public const string ArgumentoAplicar = "--aplicar";

        public const int ExitSucesso = 0;
        public const int ExitFalha = 1;
        public const int ExitConfiguracaoAusente = 2;
        /// <summary>Concluído, mas ficaram valores que exigem atenção (ilegíveis ou ambíguos).</summary>
        public const int ExitPendencias = 3;

        public static bool FoiSolicitado(string[] args) =>
            args.Any(a => string.Equals(a, Argumento, StringComparison.OrdinalIgnoreCase));

        public sealed record Registro(int Id, string? Valor);

        public sealed record ResultadoColuna(
            string Coluna, int NaoCifrados, int Atual, int Anterior, int Ambas, int Nenhuma, int Recifrados);

        private sealed record Coluna(
            string Nome,
            Func<TratooContext, IQueryable<Registro>> Ler,
            Func<TratooContext, int, string, string, CancellationToken, Task<int>> Gravar);

        /// <summary>Todas as colunas gravadas com <see cref="DataProtector.Encrypt(string)"/>.</summary>
        private static readonly Coluna[] Colunas =
        {
            new("ContasBancarias.PixChave",
                db => db.ContasBancarias.Select(c => new Registro(c.Id, c.PixChave)),
                (db, id, antigo, novo, ct) => db.ContasBancarias
                    .Where(c => c.Id == id && c.PixChave == antigo)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.PixChave, novo), ct)),
            new("ContasBancarias.ContaCriptografada",
                db => db.ContasBancarias.Select(c => new Registro(c.Id, c.ContaCriptografada)),
                (db, id, antigo, novo, ct) => db.ContasBancarias
                    .Where(c => c.Id == id && c.ContaCriptografada == antigo)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ContaCriptografada, novo), ct)),
            new("UserIdentities.CpfCnpjCriptografado",
                db => db.UserIdentities.Select(u => new Registro(u.Id, u.CpfCnpjCriptografado)),
                (db, id, antigo, novo, ct) => db.UserIdentities
                    .Where(u => u.Id == id && u.CpfCnpjCriptografado == antigo)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.CpfCnpjCriptografado, novo), ct)),
            new("UserIdentities.ChavePixCriptografada",
                db => db.UserIdentities.Where(u => u.ChavePixCriptografada != null)
                    .Select(u => new Registro(u.Id, u.ChavePixCriptografada)),
                (db, id, antigo, novo, ct) => db.UserIdentities
                    .Where(u => u.Id == id && u.ChavePixCriptografada == antigo)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.ChavePixCriptografada, novo), ct)),
            new("UserIdentities.CpfRepresentanteLegalCriptografado",
                db => db.UserIdentities.Where(u => u.CpfRepresentanteLegalCriptografado != null)
                    .Select(u => new Registro(u.Id, u.CpfRepresentanteLegalCriptografado)),
                (db, id, antigo, novo, ct) => db.UserIdentities
                    .Where(u => u.Id == id && u.CpfRepresentanteLegalCriptografado == antigo)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.CpfRepresentanteLegalCriptografado, novo), ct)),
        };

        public static async Task<int> ExecutarAsync(string[] args)
        {
            var aplicar = args.Any(a => string.Equals(a, ArgumentoAplicar, StringComparison.OrdinalIgnoreCase));
            var configuration = ModoMigracao.LerConfiguracao();

            using var loggerFactory = LoggerFactory.Create(b => b
                .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
                .AddFilter("Microsoft", LogLevel.Warning)
                // O SQL do EF leva os valores cifrados como parâmetro; mesmo cifrados, não vão para o log.
                .AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None));
            var logger = loggerFactory.CreateLogger("Tratoo.Recifragem");

            var conexao = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(conexao))
            {
                logger.LogError("Recifragem abortada: configure ConnectionStrings:DefaultConnection.");
                return ExitConfiguracaoAusente;
            }

            ChavesDeDados chaves;
            try
            {
                chaves = DataProtector.PrepararChaves(
                    configuration[DataProtector.ChaveConfiguracao],
                    configuration[DataProtector.ChaveAnteriorConfiguracao]);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError("Recifragem abortada: {Motivo}", ex.Message);
                return ExitConfiguracaoAusente;
            }

            if (chaves.Anterior is null)
            {
                logger.LogError(
                    "Recifragem abortada: defina {Anterior} com a chave antiga e {Atual} com a nova (Docs/ROTACAO-CHAVE-DADOS.md).",
                    DataProtector.VariavelDeAmbienteAnterior, DataProtector.VariavelDeAmbiente);
                return ExitConfiguracaoAusente;
            }

            try
            {
                logger.LogInformation("{Modo} em {Alvo}.",
                    aplicar ? "Recifragem (APLICANDO)" : "Recifragem — só relatório, nada será gravado",
                    ModoMigracao.Descrever(conexao));

                var opcoes = new DbContextOptionsBuilder<TratooContext>()
                    .UseNpgsql(conexao)
                    .UseLoggerFactory(loggerFactory)
                    .Options;

                await using var db = new TratooContext(opcoes);

                var resultado = await RecifrarAsync(db, chaves, aplicar);
                Registrar(logger, aplicar ? "Antes / recifrados" : "Situação atual", resultado);

                if (!aplicar)
                {
                    var faltam = resultado.Sum(r => r.Anterior);
                    logger.LogInformation(faltam == 0
                        ? "Nenhum valor na chave anterior."
                        : "{Faltam} valor(es) na chave anterior. Rode de novo com " + ArgumentoAplicar + " para recifrar.", faltam);
                    return ExitSucesso;
                }

                // Conferência independente: relê tudo do banco depois do commit.
                db.ChangeTracker.Clear();
                var depois = await RecifrarAsync(db, chaves, aplicar: false);
                Registrar(logger, "Depois (relido do banco)", depois);

                if (depois.Any(r => r.Anterior > 0 || r.Ambas > 0 || r.Nenhuma > 0))
                {
                    logger.LogWarning(
                        "Ficaram valores fora da chave atual. NÃO remova {Anterior} até resolvê-los.",
                        DataProtector.VariavelDeAmbienteAnterior);
                    return ExitPendencias;
                }

                logger.LogInformation(
                    "Recifragem concluída: todos os valores cifrados estão na chave atual. {Anterior} já pode sair da configuração.",
                    DataProtector.VariavelDeAmbienteAnterior);
                return ExitSucesso;
            }
            catch (Exception ex)
            {
                logger.LogError("Recifragem falhou e foi desfeita ({Tipo}: {Mensagem}). Nada foi alterado.",
                    ex.GetType().Name, ex.Message);
                return ExitFalha;
            }
        }

        /// <summary>
        /// Classifica todos os valores e, se <paramref name="aplicar"/>, recifra os que estão
        /// na chave anterior numa única transação. Lança (e desfaz tudo) se um valor mudou
        /// no banco entre a leitura e a gravação.
        /// </summary>
        public static async Task<IReadOnlyList<ResultadoColuna>> RecifrarAsync(
            TratooContext db, ChavesDeDados chaves, bool aplicar, CancellationToken ct = default)
        {
            if (chaves.Anterior is null)
                throw new ArgumentException("A recifragem precisa da chave anterior.", nameof(chaves));

            await using var transacao = aplicar ? await db.Database.BeginTransactionAsync(ct) : null;
            var resultados = new List<ResultadoColuna>();

            foreach (var coluna in Colunas)
            {
                var registros = await coluna.Ler(db).AsNoTracking().ToListAsync(ct);
                int naoCifrados = 0, atual = 0, anterior = 0, ambas = 0, nenhuma = 0, recifrados = 0;

                foreach (var r in registros)
                {
                    switch (DataProtector.Identificar(r.Valor, chaves))
                    {
                        case ChaveDoValor.NaoCifrado: naoCifrados++; break;
                        case ChaveDoValor.Atual: atual++; break;
                        case ChaveDoValor.Ambas: ambas++; break;
                        case ChaveDoValor.Nenhuma: nenhuma++; break;
                        case ChaveDoValor.Anterior:
                            anterior++;
                            if (!aplicar)
                                break;

                            var novo = Recifrar(r.Valor!, chaves);
                            var gravados = await coluna.Gravar(db, r.Id, r.Valor!, novo, ct);
                            if (gravados != 1)
                                throw new InvalidOperationException(
                                    $"{coluna.Nome} (Id {r.Id}) mudou durante a recifragem. Rode o comando de novo.");
                            recifrados++;
                            break;
                    }
                }

                resultados.Add(new ResultadoColuna(coluna.Nome, naoCifrados, atual, anterior, ambas, nenhuma, recifrados));
            }

            if (transacao is not null)
                await transacao.CommitAsync(ct);

            return resultados;
        }

        /// <summary>
        /// Decifra com a anterior e cifra com a atual, conferindo o resultado antes de devolvê-lo:
        /// o valor novo tem de decifrar com a atual para o MESMO texto e não pode ser
        /// confundível com a anterior.
        /// </summary>
        private static string Recifrar(string valor, ChavesDeDados chaves)
        {
            if (!DataProtector.TentarDecifrar(valor, chaves.Anterior!, out var claro))
                throw new InvalidOperationException("Valor classificado como da chave anterior não decifrou com ela.");

            // IV aleatório: na chance ínfima de o valor novo também "decifrar" com a anterior,
            // gera outro.
            for (var tentativa = 0; tentativa < 5; tentativa++)
            {
                var novo = DataProtector.Encrypt(claro, chaves.Atual);
                if (DataProtector.Identificar(novo, chaves) == ChaveDoValor.Atual
                    && DataProtector.TentarDecifrar(novo, chaves.Atual, out var conferido)
                    && string.Equals(conferido, claro, StringComparison.Ordinal))
                    return novo;
            }

            throw new InvalidOperationException("Não foi possível produzir um valor recifrado conferido.");
        }

        private static void Registrar(ILogger logger, string titulo, IReadOnlyList<ResultadoColuna> resultado)
        {
            logger.LogInformation("{Titulo}:", titulo);
            foreach (var r in resultado)
                logger.LogInformation(
                    "  {Coluna}: atual={Atual} anterior={Anterior} recifrados={Recifrados} ilegíveis={Nenhuma} ambíguos={Ambas} não-cifrados={NaoCifrados}",
                    r.Coluna, r.Atual, r.Anterior, r.Recifrados, r.Nenhuma, r.Ambas, r.NaoCifrados);
        }
    }
}
