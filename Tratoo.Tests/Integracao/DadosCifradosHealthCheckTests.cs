using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Features.Shared;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// O check de readiness "dados-cifrados" contra Postgres REAL: é ele que impede um deploy
    /// com a chave de dados errada de ser promovido. "Chave errada" é simulada do jeito que
    /// acontece na prática — o banco tem dados cifrados com UMA chave e o processo está
    /// configurado com OUTRA (a da suíte de testes).
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class DadosCifradosHealthCheckTests
    {
        private readonly PostgresFixture _pg;

        public DadosCifradosHealthCheckTests(PostgresFixture pg) => _pg = pg;

        private static readonly byte[] OutraChave =
            DataProtector.DerivarChave("uma-chave-diferente-da-configurada-no-processo");

        private static async Task<HealthCheckResult> ConferirAsync(string conexao, EstadoChaveDados? estado = null)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            return await new DadosCifradosHealthCheck(db, estado ?? new EstadoChaveDados())
                .CheckHealthAsync(new HealthCheckContext());
        }

        /// <summary>Regrava os dados bancários de um prestador como se tivessem sido cifrados com outra chave.</summary>
        private static async Task RecifrarComOutraChaveAsync(string conexao, int prestadorId)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            var pix = DataProtector.Encrypt(Guid.NewGuid().ToString(), OutraChave);
            var conta = DataProtector.Encrypt("654321", OutraChave);
            await db.ContasBancarias.Where(c => c.PrestadorId == prestadorId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PixChave, pix).SetProperty(c => c.ContaCriptografada, conta));
        }

        [FactComDocker]
        public async Task Banco_sem_dado_cifrado_fica_saudavel()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();

            var resultado = await ConferirAsync(conexao);

            Assert.Equal(HealthStatus.Healthy, resultado.Status);
            Assert.Contains("Nenhum dado cifrado", resultado.Description);
        }

        [FactComDocker]
        public async Task Chave_certa_decifra_as_amostras_e_o_resultado_fica_lembrado()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await SeedIntegracao.CriarAsync(conexao);   // conta bancária cifrada com a chave do processo
            var estado = new EstadoChaveDados();

            var resultado = await ConferirAsync(conexao, estado);

            Assert.Equal(HealthStatus.Healthy, resultado.Status);
            Assert.Contains("2 de 2", resultado.Description);
            Assert.True(estado.Confirmada);

            // Confirmada uma vez, não volta ao banco: nem um banco inexistente muda a resposta.
            var depois = await ConferirAsync("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1", estado);
            Assert.Equal(HealthStatus.Healthy, depois.Status);
        }

        [FactComDocker]
        public async Task Chave_errada_deixa_a_readiness_nao_saudavel_e_nao_vaza_dado()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            // Várias contas, todas cifradas com outra chave: é o cenário "variável errada na Railway".
            for (var i = 0; i < 3; i++)
            {
                var cenario = await SeedIntegracao.CriarAsync(conexao);
                await RecifrarComOutraChaveAsync(conexao, cenario.PrestadorId);
            }
            var estado = new EstadoChaveDados();

            var resultado = await ConferirAsync(conexao, estado);

            Assert.Equal(HealthStatus.Unhealthy, resultado.Status);   // 503 no /health/ready → deploy não é promovido
            Assert.Contains("0 de 6", resultado.Description);
            Assert.Contains(DataProtector.VariavelDeAmbiente, resultado.Description);
            Assert.False(estado.Confirmada);
            Assert.Null(resultado.Exception);
        }

        [FactComDocker]
        public async Task Dados_de_mais_de_uma_chave_ficam_degradados_sem_derrubar_a_readiness()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await SeedIntegracao.CriarAsync(conexao);                          // chave do processo
            var outro = await SeedIntegracao.CriarAsync(conexao);
            await RecifrarComOutraChaveAsync(conexao, outro.PrestadorId);       // outra chave

            var resultado = await ConferirAsync(conexao);

            Assert.Equal(HealthStatus.Degraded, resultado.Status);   // 200: visível, mas não bloqueia (rotação em curso)
            Assert.Contains("2 de 4", resultado.Description);
        }

        [FactComDocker]
        public async Task Valores_ficticios_de_seed_nao_contam_como_dado_cifrado()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                // Como o seed de desenvolvimento grava: texto fixo, não um valor cifrado.
                db.UserIdentities.Add(new UserIdentity
                {
                    UserId = cenario.PrestadorId,
                    CpfCnpjCriptografado = "SEED_ENCRYPTED_CPF_PRESTADOR",
                    NomeLegal = "Prestador Seed"
                });
                await db.SaveChangesAsync();
            }

            var resultado = await ConferirAsync(conexao);

            Assert.Equal(HealthStatus.Healthy, resultado.Status);
            Assert.Contains("2 de 2", resultado.Description);   // só a conta bancária real entrou na conta
        }
    }
}
