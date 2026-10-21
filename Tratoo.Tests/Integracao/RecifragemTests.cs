using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Features.Shared;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Rotação da chave de dados contra Postgres REAL. O cenário é o de produção: os dados
    /// existentes estão na chave ANTERIOR (aqui, a chave da suíte, com que o seed cifra) e a
    /// chave ATUAL é uma nova. O <see cref="ModoRecifragem"/> precisa regravar tudo na nova
    /// sem perder nenhum valor, sem tocar no que não é dele e sem gravar nada no modo relatório.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class RecifragemTests
    {
        private readonly PostgresFixture _pg;

        public RecifragemTests(PostgresFixture pg) => _pg = pg;

        private static readonly ChavesDeDados Rotacao =
            DataProtector.PrepararChaves("chave-nova-da-integracao-de-rotacao-01", ConfiguracaoDosTestes.ChaveDeDadosDeTeste);

        private static readonly byte[] Terceira = DataProtector.DerivarChave("terceira-chave-que-ninguem-conhece-0123");

        private sealed record Linha(string Coluna, int Id, string? Valor);

        /// <summary>Todos os valores das colunas cifradas, como estão no banco.</summary>
        private static async Task<List<Linha>> LerTudoAsync(string conexao)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            var linhas = new List<Linha>();
            foreach (var c in await db.ContasBancarias.AsNoTracking().OrderBy(c => c.Id).ToListAsync())
            {
                linhas.Add(new("PixChave", c.Id, c.PixChave));
                linhas.Add(new("ContaCriptografada", c.Id, c.ContaCriptografada));
            }
            foreach (var u in await db.UserIdentities.AsNoTracking().OrderBy(u => u.Id).ToListAsync())
            {
                linhas.Add(new("CpfCnpj", u.Id, u.CpfCnpjCriptografado));
                linhas.Add(new("ChavePix", u.Id, u.ChavePixCriptografada));
                linhas.Add(new("CpfRepresentante", u.Id, u.CpfRepresentanteLegalCriptografado));
            }
            return linhas;
        }

        /// <summary>
        /// Banco com: 2 contas bancárias na chave anterior (seed); uma identidade PJ completa na
        /// anterior; uma identidade já na chave atual; uma com valor fictício de seed.
        /// </summary>
        private static async Task<string> CenarioDeRotacaoAsync(PostgresFixture pg)
        {
            var conexao = await pg.CriarBancoMigradoAsync();
            var a = await SeedIntegracao.CriarAsync(conexao);
            var b = await SeedIntegracao.CriarAsync(conexao);

            await using var db = SeedIntegracao.Contexto(conexao);
            db.UserIdentities.AddRange(
                new UserIdentity
                {
                    UserId = a.PrestadorId, NomeLegal = "Empresa PJ",
                    CpfCnpjCriptografado = DataProtector.Encrypt("11222333000181", Rotacao.Anterior!),
                    ChavePixCriptografada = DataProtector.Encrypt("pix@empresa.com.br", Rotacao.Anterior!),
                    CpfRepresentanteLegalCriptografado = DataProtector.Encrypt("12345678909", Rotacao.Anterior!)
                },
                new UserIdentity
                {
                    UserId = a.ContratanteId, NomeLegal = "Já na chave nova",
                    CpfCnpjCriptografado = DataProtector.Encrypt("98765432100", Rotacao.Atual)
                },
                new UserIdentity
                {
                    UserId = b.PrestadorId, NomeLegal = "Seed",
                    CpfCnpjCriptografado = "SEED_ENCRYPTED_CPF_PRESTADOR"
                });
            await db.SaveChangesAsync();
            return conexao;
        }

        [FactComDocker]
        public async Task Relatorio_classifica_tudo_e_nao_grava_nada()
        {
            var conexao = await CenarioDeRotacaoAsync(_pg);
            var antes = await LerTudoAsync(conexao);

            await using var db = SeedIntegracao.Contexto(conexao);
            var resultado = await ModoRecifragem.RecifrarAsync(db, Rotacao, aplicar: false);

            Assert.Equal(antes, await LerTudoAsync(conexao));       // nada mudou
            Assert.Equal(7, resultado.Sum(r => r.Anterior));        // 2 contas × 2 colunas + PJ × 3 colunas
            Assert.Equal(1, resultado.Sum(r => r.Atual));
            Assert.Equal(1, resultado.Sum(r => r.NaoCifrados));
            Assert.Equal(0, resultado.Sum(r => r.Recifrados));
            Assert.Equal(0, resultado.Sum(r => r.Nenhuma + r.Ambas));
        }

        [FactComDocker]
        public async Task Aplicar_recifra_sem_perder_valor_e_e_idempotente()
        {
            var conexao = await CenarioDeRotacaoAsync(_pg);
            var antes = await LerTudoAsync(conexao);
            var clarosAntes = antes.Where(l => DataProtector.PareceCifrado(l.Valor))
                .ToDictionary(l => (l.Coluna, l.Id), l => DataProtector.Decrypt(l.Valor!, Rotacao.Atual, Rotacao.Anterior!));

            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var resultado = await ModoRecifragem.RecifrarAsync(db, Rotacao, aplicar: true);
                Assert.Equal(7, resultado.Sum(r => r.Recifrados));
            }

            var depois = await LerTudoAsync(conexao);
            foreach (var l in depois)
            {
                var original = antes.Single(x => x.Coluna == l.Coluna && x.Id == l.Id);
                if (!DataProtector.PareceCifrado(original.Valor))
                {
                    Assert.Equal(original.Valor, l.Valor);   // nulos e valores de seed intocados
                    continue;
                }

                // Tudo na chave nova, decifrando com ELA SOZINHA para o mesmo texto de antes.
                Assert.Equal(ChaveDoValor.Atual, DataProtector.Identificar(l.Valor, Rotacao));
                Assert.Equal(clarosAntes[(l.Coluna, l.Id)], DataProtector.Decrypt(l.Valor!, Rotacao.Atual));
            }

            // Rodar de novo não muda nada.
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var segunda = await ModoRecifragem.RecifrarAsync(db, Rotacao, aplicar: true);
                Assert.Equal(0, segunda.Sum(r => r.Recifrados + r.Anterior));
            }
            Assert.Equal(depois, await LerTudoAsync(conexao));
        }

        [FactComDocker]
        public async Task Valor_ilegivel_e_contado_e_deixado_como_esta()
        {
            var conexao = await CenarioDeRotacaoAsync(_pg);
            int idIlegivel;
            string ilegivel = DataProtector.Encrypt("55566677788", Terceira);
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var identidade = await db.UserIdentities.FirstAsync(u => u.NomeLegal == "Seed");
                identidade.CpfCnpjCriptografado = ilegivel;
                await db.SaveChangesAsync();
                idIlegivel = identidade.Id;
            }

            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var resultado = await ModoRecifragem.RecifrarAsync(db, Rotacao, aplicar: true);
                Assert.Equal(1, resultado.Sum(r => r.Nenhuma));
                Assert.Equal(7, resultado.Sum(r => r.Recifrados));   // o resto segue normalmente
            }

            var depois = await LerTudoAsync(conexao);
            Assert.Equal(ilegivel, depois.Single(l => l.Coluna == "CpfCnpj" && l.Id == idIlegivel).Valor);
        }

        [FactComDocker]
        public async Task Health_check_mostra_a_rotacao_em_curso_e_fica_saudavel_quando_termina()
        {
            var conexao = await CenarioDeRotacaoAsync(_pg);

            async Task<HealthCheckResult> ConferirAsync()
            {
                await using var db = SeedIntegracao.Contexto(conexao);
                return await new DadosCifradosHealthCheck(db, new EstadoChaveDados(), Rotacao)
                    .CheckHealthAsync(new HealthCheckContext());
            }

            var durante = await ConferirAsync();
            Assert.Equal(HealthStatus.Degraded, durante.Status);        // 200: não derruba o deploy
            Assert.Contains("ainda na chave anterior", durante.Description);
            Assert.Contains("--recifrar-dados", durante.Description);

            await using (var db = SeedIntegracao.Contexto(conexao))
                await ModoRecifragem.RecifrarAsync(db, Rotacao, aplicar: true);

            var depois = await ConferirAsync();
            Assert.Equal(HealthStatus.Healthy, depois.Status);
            Assert.Contains("amostras decifradas", depois.Description);

            // Depois de remover a chave anterior da configuração, tudo continua legível.
            await using var semAnterior = SeedIntegracao.Contexto(conexao);
            var final = await new DadosCifradosHealthCheck(semAnterior, new EstadoChaveDados(),
                    DataProtector.PrepararChaves("chave-nova-da-integracao-de-rotacao-01", null))
                .CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, final.Status);
        }
    }
}
