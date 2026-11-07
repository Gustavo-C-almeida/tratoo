using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Features.Shared;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Recifragem: <c>dotnet Tratoo.API.dll --recifrar-dados [--aplicar]</c> regrava, com a
    /// chave ATUAL (<c>DataProtector:Chave</c>) e no formato v2 (AES-GCM):
    ///  • todo valor ainda cifrado com a ANTERIOR (<c>DataProtector:ChaveAnterior</c>), numa
    ///    rotação de chave;
    ///  • todo valor da chave atual ainda no formato antigo (AES-CBC) — migração de formato,
    ///    que não exige chave anterior.
    /// Roteiro completo em Docs/ROTACAO-CHAVES.md.
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

        /// <param name="Atual">Valores na chave atual, em qualquer formato.</param>
        /// <param name="FormatoAntigo">Dos valores na chave atual, quantos ainda estão em AES-CBC.</param>
        /// <param name="Recifrados">Regravados nesta execução (da anterior ou do formato antigo).</param>
        public sealed record ResultadoColuna(
            string Coluna, int NaoCifrados, int Atual, int Anterior, int Ambas, int Nenhuma, int Recifrados,
            int FormatoAntigo)
        {
            /// <summary>Valores que a recifragem ainda tem de regravar.</summary>
            public int Pendentes => Anterior + FormatoAntigo;
        }

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

            try
            {
                logger.LogInformation("{Modo} em {Alvo}. {Escopo}.",
                    aplicar ? "Recifragem (APLICANDO)" : "Recifragem — só relatório, nada será gravado",
                    ModoMigracao.Descrever(conexao),
                    chaves.Anterior is null
                        ? "Sem chave anterior: só migração de formato (AES-CBC → v2)"
                        : "Rotação de chave + migração de formato");

                var opcoes = new DbContextOptionsBuilder<TratooContext>()
                    .UseNpgsql(conexao)
                    .UseLoggerFactory(loggerFactory)
                    .Options;

                await using var db = new TratooContext(opcoes);

                var resultado = await RecifrarAsync(db, chaves, aplicar);
                Registrar(logger, aplicar ? "Antes / recifrados" : "Situação atual", resultado);

                if (!aplicar)
                {
                    var faltam = resultado.Sum(r => r.Pendentes);
                    logger.LogInformation(faltam == 0
                        ? "Nada a recifrar: tudo na chave atual e no formato v2."
                        : "{Faltam} valor(es) a recifrar. Rode de novo com " + ArgumentoAplicar + " para recifrar.", faltam);
                    return ExitSucesso;
                }

                // Conferência independente: relê tudo do banco depois do commit.
                db.ChangeTracker.Clear();
                var depois = await RecifrarAsync(db, chaves, aplicar: false);
                Registrar(logger, "Depois (relido do banco)", depois);

                if (depois.Any(r => r.Pendentes > 0 || r.Ambas > 0 || r.Nenhuma > 0))
                {
                    logger.LogWarning(
                        "Ficaram valores fora da chave atual ou do formato v2. NÃO remova {Anterior} até resolvê-los.",
                        DataProtector.VariavelDeAmbienteAnterior);
                    return ExitPendencias;
                }

                logger.LogInformation(chaves.Anterior is null
                        ? "Recifragem concluída: todos os valores cifrados estão no formato v2."
                        : "Recifragem concluída: todos os valores cifrados estão na chave atual e no formato v2. {Anterior} já pode sair da configuração.",
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
        /// Classifica todos os valores e, se <paramref name="aplicar"/>, recifra numa única
        /// transação os que estão na chave anterior ou no formato antigo. Lança (e desfaz
        /// tudo) se um valor mudou no banco entre a leitura e a gravação.
        /// </summary>
        public static async Task<IReadOnlyList<ResultadoColuna>> RecifrarAsync(
            TratooContext db, ChavesDeDados chaves, bool aplicar, CancellationToken ct = default)
        {
            await using var transacao = aplicar ? await db.Database.BeginTransactionAsync(ct) : null;
            var resultados = new List<ResultadoColuna>();

            foreach (var coluna in Colunas)
            {
                var registros = await coluna.Ler(db).AsNoTracking().ToListAsync(ct);
                int naoCifrados = 0, atual = 0, anterior = 0, ambas = 0, nenhuma = 0, recifrados = 0, formatoAntigo = 0;

                foreach (var r in registros)
                {
                    switch (DataProtector.Identificar(r.Valor, chaves))
                    {
                        case ChaveDoValor.NaoCifrado: naoCifrados++; break;
                        case ChaveDoValor.Ambas: ambas++; break;
                        case ChaveDoValor.Nenhuma: nenhuma++; break;
                        case ChaveDoValor.Atual:
                            atual++;
                            if (DataProtector.EhV2(r.Valor))
                                break;
                            formatoAntigo++;
                            if (aplicar)
                            {
                                await GravarAsync(coluna, db, r, Recifrar(r.Valor!, chaves.Atual, chaves), ct);
                                recifrados++;
                            }
                            break;
                        case ChaveDoValor.Anterior:
                            anterior++;
                            if (aplicar)
                            {
                                await GravarAsync(coluna, db, r, Recifrar(r.Valor!, chaves.Anterior!, chaves), ct);
                                recifrados++;
                            }
                            break;
                    }
                }

                resultados.Add(new ResultadoColuna(coluna.Nome, naoCifrados, atual, anterior, ambas, nenhuma, recifrados, formatoAntigo));
            }

            if (transacao is not null)
                await transacao.CommitAsync(ct);

            return resultados;
        }

        private static async Task GravarAsync(
            Coluna coluna, TratooContext db, Registro r, string novo, CancellationToken ct)
        {
            var gravados = await coluna.Gravar(db, r.Id, r.Valor!, novo, ct);
            if (gravados != 1)
                throw new InvalidOperationException(
                    $"{coluna.Nome} (Id {r.Id}) mudou durante a recifragem. Rode o comando de novo.");
        }

        /// <summary>
        /// Decifra com a chave de origem e cifra com a atual em v2, conferindo o resultado
        /// antes de devolvê-lo: o valor novo tem de ser v2, decifrar com a atual para o MESMO
        /// texto e ser identificado como da atual.
        /// </summary>
        private static string Recifrar(string valor, byte[] chaveDeOrigem, ChavesDeDados chaves)
        {
            if (!DataProtector.TentarDecifrar(valor, chaveDeOrigem, out var claro))
                throw new InvalidOperationException("Valor classificado numa chave não decifrou com ela.");

            var novo = DataProtector.Encrypt(claro, chaves.Atual);
            if (DataProtector.EhV2(novo)
                && DataProtector.Identificar(novo, chaves) == ChaveDoValor.Atual
                && DataProtector.TentarDecifrar(novo, chaves.Atual, out var conferido)
                && string.Equals(conferido, claro, StringComparison.Ordinal))
                return novo;

            throw new InvalidOperationException("Não foi possível produzir um valor recifrado conferido.");
        }

        private static void Registrar(ILogger logger, string titulo, IReadOnlyList<ResultadoColuna> resultado)
        {
            logger.LogInformation("{Titulo}:", titulo);
            foreach (var r in resultado)
                logger.LogInformation(
                    "  {Coluna}: atual={Atual} (formato antigo={FormatoAntigo}) anterior={Anterior} recifrados={Recifrados} ilegíveis={Nenhuma} ambíguos={Ambas} não-cifrados={NaoCifrados}",
                    r.Coluna, r.Atual, r.FormatoAntigo, r.Anterior, r.Recifrados, r.Nenhuma, r.Ambas, r.NaoCifrados);
        }
    }
}
