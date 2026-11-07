using Microsoft.EntityFrameworkCore;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Features.Shared;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Migração de formato AES-CBC → v2 (AES-GCM) pela recifragem, contra Postgres REAL:
    /// com e sem troca de chave, sob concorrência e com falha no meio. Em nenhum caso um
    /// valor pode ficar ilegível, mudar de texto ou ficar meio gravado.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class RecifragemFormatoTests
    {
        private readonly PostgresFixture _pg;

        public RecifragemFormatoTests(PostgresFixture pg) => _pg = pg;

        private static readonly byte[] ChaveAntiga = DataProtector.DerivarChave("chave-antiga-do-teste-de-formato-0001");
        private static readonly byte[] ChaveNova = DataProtector.DerivarChave("chave-nova-do-teste-de-formato-00002");
        private static readonly ChavesDeDados SoAtual =
            DataProtector.PrepararChaves("chave-antiga-do-teste-de-formato-0001", null);
        private static readonly ChavesDeDados Rotacao =
            DataProtector.PrepararChaves("chave-nova-do-teste-de-formato-00002", "chave-antiga-do-teste-de-formato-0001");

        private sealed record Linha(int Id, string NomeLegal, string? Cpf, string? Pix);

        private static async Task<List<Linha>> LerAsync(string conexao)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            return await db.UserIdentities.AsNoTracking().OrderBy(u => u.Id)
                .Select(u => new Linha(u.Id, u.NomeLegal, u.CpfCnpjCriptografado, u.ChavePixCriptografada))
                .ToListAsync();
        }

        /// <summary>
        /// Identidades com, por linha: CPF em CBC e PIX em v2, na chave <paramref name="chave"/>;
        /// mais uma com valor fictício de seed. Devolve os textos claros esperados.
        /// </summary>
        private static async Task<(string Conexao, Dictionary<(int, string), string> Claros)> CenarioAsync(
            PostgresFixture pg, byte[] chave, int linhas = 6, Func<int, byte[]>? chavePorLinha = null)
        {
            var conexao = await pg.CriarBancoMigradoAsync();
            var claros = new Dictionary<(int, string), string>();
            var ids = new List<int>();
            for (var i = 0; i < linhas; i++)
                ids.Add((await SeedIntegracao.CriarAsync(conexao)).PrestadorId);

            await using var db = SeedIntegracao.Contexto(conexao);
            var entidades = new List<UserIdentity>();
            for (var i = 0; i < linhas; i++)
            {
                var k = chavePorLinha?.Invoke(i) ?? chave;
                entidades.Add(new UserIdentity
                {
                    UserId = ids[i], NomeLegal = $"Pessoa {i}",
                    CpfCnpjCriptografado = DataProtector.EncryptLegado($"1234567890{i}", k),
                    ChavePixCriptografada = DataProtector.Encrypt($"pix{i}@exemplo.com", k)
                });
            }
            entidades.Add(new UserIdentity
            {
                UserId = (await SeedIntegracao.CriarAsync(conexao)).PrestadorId,
                NomeLegal = "Seed", CpfCnpjCriptografado = "SEED_ENCRYPTED_CPF_PRESTADOR"
            });
            db.UserIdentities.AddRange(entidades);
            await db.SaveChangesAsync();

            for (var i = 0; i < linhas; i++)
            {
                claros[(entidades[i].Id, "Cpf")] = $"1234567890{i}";
                claros[(entidades[i].Id, "Pix")] = $"pix{i}@exemplo.com";
            }
            return (conexao, claros);
        }

        private static void ConferirTudoEmV2(List<Linha> linhas, Dictionary<(int, string), string> claros, byte[] chaveAtual)
        {
            foreach (var l in linhas)
            {
                if (l.NomeLegal == "Seed")
                {
                    Assert.Equal("SEED_ENCRYPTED_CPF_PRESTADOR", l.Cpf);   // intocado
                    continue;
                }
                foreach (var (campo, valor) in new[] { ("Cpf", l.Cpf), ("Pix", l.Pix) })
                {
                    Assert.True(DataProtector.EhV2(valor), $"{campo} da linha {l.Id} não está em v2");
                    Assert.Equal(claros[(l.Id, campo)], DataProtector.Decrypt(valor!, chaveAtual));
                }
            }
        }

        [FactComDocker]
        public async Task Sem_chave_anterior_relatorio_conta_o_formato_antigo_e_nao_grava()
        {
            var (conexao, _) = await CenarioAsync(_pg, ChaveAntiga);
            var antes = await LerAsync(conexao);

            await using var db = SeedIntegracao.Contexto(conexao);
            // Só as colunas do cenário: as contas bancárias do seed estão na chave da suíte.
            var r = (await ModoRecifragem.RecifrarAsync(db, SoAtual, aplicar: false))
                .Where(c => c.Coluna.StartsWith("UserIdentities.", StringComparison.Ordinal)).ToList();

            Assert.Equal(antes, await LerAsync(conexao));
            Assert.Equal(6, r.Sum(c => c.FormatoAntigo));      // os CPFs em CBC
            Assert.Equal(12, r.Sum(c => c.Atual));             // CPF + PIX, todos na chave atual
            Assert.Equal(0, r.Sum(c => c.Anterior + c.Recifrados + c.Nenhuma + c.Ambas));
        }

        [FactComDocker]
        public async Task Sem_chave_anterior_aplicar_converte_CBC_para_v2_sem_tocar_no_que_ja_e_v2_e_e_idempotente()
        {
            var (conexao, claros) = await CenarioAsync(_pg, ChaveAntiga);
            var antes = await LerAsync(conexao);

            await using (var db = SeedIntegracao.Contexto(conexao))
                Assert.Equal(6, (await ModoRecifragem.RecifrarAsync(db, SoAtual, aplicar: true)).Sum(c => c.Recifrados));

            var depois = await LerAsync(conexao);
            ConferirTudoEmV2(depois, claros, ChaveAntiga);
            // O PIX já era v2: o valor gravado é exatamente o mesmo de antes.
            Assert.Equal(antes.Select(l => l.Pix), depois.Select(l => l.Pix));

            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var segunda = await ModoRecifragem.RecifrarAsync(db, SoAtual, aplicar: true);
                Assert.Equal(0, segunda.Sum(c => c.Recifrados + c.Pendentes));
            }
            Assert.Equal(depois, await LerAsync(conexao));
        }

        [FactComDocker]
        public async Task Rotacao_converte_os_dois_formatos_das_duas_chaves_para_v2_na_atual()
        {
            // Linhas pares na chave antiga, ímpares já na nova: CBC e v2 em cada uma.
            var (conexao, claros) = await CenarioAsync(_pg, ChaveAntiga,
                chavePorLinha: i => i % 2 == 0 ? ChaveAntiga : ChaveNova);

            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var r = await ModoRecifragem.RecifrarAsync(db, Rotacao, aplicar: true);
                Assert.Equal(6, r.Sum(c => c.Anterior));        // 3 linhas × (CPF CBC + PIX v2)
                Assert.Equal(3, r.Sum(c => c.FormatoAntigo));   // CPFs CBC já na nova
                Assert.Equal(9, r.Sum(c => c.Recifrados));
            }

            ConferirTudoEmV2(await LerAsync(conexao), claros, ChaveNova);
            await using var conferencia = SeedIntegracao.Contexto(conexao);
            Assert.Equal(0, (await ModoRecifragem.RecifrarAsync(conferencia, Rotacao, aplicar: false)).Sum(c => c.Pendentes));
        }

        [FactComDocker]
        public async Task Duas_recifragens_simultaneas_nao_perdem_nem_corrompem_valor()
        {
            var (conexao, claros) = await CenarioAsync(_pg, ChaveAntiga, linhas: 20);

            async Task<Exception?> RodarAsync()
            {
                try
                {
                    await using var db = SeedIntegracao.Contexto(conexao);
                    await ModoRecifragem.RecifrarAsync(db, SoAtual, aplicar: true);
                    return null;
                }
                catch (Exception ex) { return ex; }
            }

            var resultados = await Task.WhenAll(RodarAsync(), RodarAsync());

            // No máximo uma desiste (valor mudou sob ela → desfaz tudo); nunca as duas.
            Assert.Contains(null, resultados);
            foreach (var ex in resultados.Where(e => e is not null))
                Assert.Contains("mudou durante a recifragem", ex!.Message);

            ConferirTudoEmV2(await LerAsync(conexao), claros, ChaveAntiga);
        }

        [FactComDocker]
        public async Task Falha_no_meio_desfaz_tudo_e_a_execucao_seguinte_conclui()
        {
            var (conexao, claros) = await CenarioAsync(_pg, ChaveAntiga);
            var antes = await LerAsync(conexao);

            // Simula a falha (queda de conexão, erro no banco) ao gravar a ÚLTIMA linha.
            await using (var db = SeedIntegracao.Contexto(conexao))
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION falhar_recifragem() RETURNS trigger AS $$
                    BEGIN RAISE EXCEPTION 'falha simulada'; END $$ LANGUAGE plpgsql;
                    CREATE TRIGGER falhar BEFORE UPDATE ON "UserIdentities"
                    FOR EACH ROW WHEN (OLD."NomeLegal" = 'Pessoa 5') EXECUTE FUNCTION falhar_recifragem();
                    """);

            await using (var db = SeedIntegracao.Contexto(conexao))
                await Assert.ThrowsAnyAsync<Exception>(() => ModoRecifragem.RecifrarAsync(db, SoAtual, aplicar: true));

            // Nada ficou meio gravado: o banco está exatamente como antes, e tudo legível.
            var depoisDaFalha = await LerAsync(conexao);
            Assert.Equal(antes, depoisDaFalha);
            foreach (var l in depoisDaFalha.Where(l => l.NomeLegal != "Seed"))
                Assert.Equal(claros[(l.Id, "Cpf")], DataProtector.Decrypt(l.Cpf!, ChaveAntiga));

            await using (var db = SeedIntegracao.Contexto(conexao))
                await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER falhar ON "UserIdentities";""");

            await using (var db = SeedIntegracao.Contexto(conexao))
                Assert.Equal(6, (await ModoRecifragem.RecifrarAsync(db, SoAtual, aplicar: true)).Sum(c => c.Recifrados));
            ConferirTudoEmV2(await LerAsync(conexao), claros, ChaveAntiga);
        }
    }
}
