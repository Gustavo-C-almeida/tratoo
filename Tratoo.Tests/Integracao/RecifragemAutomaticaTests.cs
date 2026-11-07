using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Tratoo.API.BackgroundServices;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Data;
using Tratoo.Domain.Features.Shared;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Recifragem automática (job dentro da API) contra Postgres REAL: converte, confere,
    /// se recupera de falha, não deixa duas réplicas rodarem juntas e nunca anuncia que a
    /// chave anterior pode sair sem uma conferência limpa.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class RecifragemAutomaticaTests
    {
        private readonly PostgresFixture _pg;

        public RecifragemAutomaticaTests(PostgresFixture pg) => _pg = pg;

        private static readonly ChavesDeDados Rotacao = DataProtector.PrepararChaves(
            "chave-nova-da-recifragem-automatica-01", "chave-antiga-da-recifragem-automatica-1");

        /// <summary>Identidades com CPF em CBC e PIX em v2, todos na chave <paramref name="chave"/>.</summary>
        private static async Task<string> CenarioAsync(PostgresFixture pg, byte[] chave, int linhas = 5)
        {
            var conexao = await pg.CriarBancoMigradoAsync();
            var ids = new List<int>();
            for (var i = 0; i < linhas; i++)
                ids.Add((await SeedIntegracao.CriarAsync(conexao)).PrestadorId);

            await using var db = SeedIntegracao.Contexto(conexao);
            for (var i = 0; i < linhas; i++)
                db.UserIdentities.Add(new UserIdentity
                {
                    UserId = ids[i], NomeLegal = $"Pessoa {i}",
                    CpfCnpjCriptografado = DataProtector.EncryptLegado($"1234567890{i}", chave),
                    ChavePixCriptografada = DataProtector.Encrypt($"pix{i}@exemplo.com", chave)
                });
            await db.SaveChangesAsync();

            // As contas do seed estão na chave da suíte: para o cenário ficar fechado, passam
            // para a mesma chave.
            foreach (var c in await db.ContasBancarias.ToListAsync())
            {
                c.PixChave = DataProtector.Encrypt(DataProtector.Decrypt(c.PixChave), chave);
                c.ContaCriptografada = DataProtector.Encrypt(DataProtector.Decrypt(c.ContaCriptografada), chave);
            }
            await db.SaveChangesAsync();
            return conexao;
        }

        private static async Task RodarAsync(string conexao, ChavesDeDados chaves, EstadoRecifragem estado)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            await RecifragemAutomaticaService.RodadaAsync(db, chaves, estado, NullLogger.Instance, CancellationToken.None);
        }

        private static async Task<int> PendentesAsync(string conexao, ChavesDeDados chaves)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            return (await ModoRecifragem.RecifrarAsync(db, chaves, aplicar: false)).Sum(r => r.Pendentes);
        }

        // ── Sucesso ──────────────────────────────────────────────────────────

        [FactComDocker]
        public async Task Rodada_converte_tudo_confere_e_so_entao_libera_a_chave_anterior()
        {
            var conexao = await CenarioAsync(_pg, Rotacao.Anterior!);
            var estado = new EstadoRecifragem();

            Assert.True(await PendentesAsync(conexao, Rotacao) > 0);
            await RodarAsync(conexao, Rotacao, estado);

            Assert.NotNull(estado.Ultima);
            Assert.True(estado.Ultima!.Concluida);
            Assert.Equal(0, await PendentesAsync(conexao, Rotacao));
            var saude = RecifragemHealthCheck.Avaliar(ligado: true, estado.Ultima);
            Assert.Equal(HealthStatus.Healthy, saude.Status);
            Assert.Contains("ChaveAnterior já pode ser removida", saude.Description);

            // Tudo decifra com a chave NOVA sozinha.
            await using var db = SeedIntegracao.Contexto(conexao);
            foreach (var u in await db.UserIdentities.AsNoTracking().ToListAsync())
            {
                Assert.True(DataProtector.EhV2(u.CpfCnpjCriptografado));
                DataProtector.Decrypt(u.CpfCnpjCriptografado, Rotacao.Atual);
            }
        }

        [FactComDocker]
        public async Task Rodada_seguinte_nao_regrava_nada()
        {
            var conexao = await CenarioAsync(_pg, Rotacao.Anterior!);
            var estado = new EstadoRecifragem();
            await RodarAsync(conexao, Rotacao, estado);

            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var antes = await db.UserIdentities.AsNoTracking().OrderBy(u => u.Id).Select(u => u.CpfCnpjCriptografado).ToListAsync();
                await RecifragemAutomaticaService.RodadaAsync(db, Rotacao, estado, NullLogger.Instance, CancellationToken.None);
                db.ChangeTracker.Clear();
                Assert.Equal(antes, await db.UserIdentities.AsNoTracking().OrderBy(u => u.Id).Select(u => u.CpfCnpjCriptografado).ToListAsync());
            }
            Assert.Equal(0, estado.Ultima!.Recifrados);
            Assert.True(estado.Ultima.Concluida);
        }

        // ── Falha e recuperação ──────────────────────────────────────────────

        [FactComDocker]
        public async Task Falha_no_meio_desfaz_tudo_avisa_no_health_e_a_rodada_seguinte_conclui()
        {
            var conexao = await CenarioAsync(_pg, Rotacao.Anterior!);
            var estado = new EstadoRecifragem();
            var pendentesAntes = await PendentesAsync(conexao, Rotacao);

            await using (var db = SeedIntegracao.Contexto(conexao))
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION falhar_recifragem() RETURNS trigger AS $$
                    BEGIN RAISE EXCEPTION 'falha simulada'; END $$ LANGUAGE plpgsql;
                    CREATE TRIGGER falhar BEFORE UPDATE ON "UserIdentities"
                    FOR EACH ROW WHEN (OLD."NomeLegal" = 'Pessoa 4') EXECUTE FUNCTION falhar_recifragem();
                    """);

            await RodarAsync(conexao, Rotacao, estado);   // não lança: registra o erro

            Assert.NotNull(estado.Ultima!.Erro);
            Assert.False(estado.Ultima.Concluida);
            var saude = RecifragemHealthCheck.Avaliar(ligado: true, estado.Ultima);
            Assert.Equal(HealthStatus.Degraded, saude.Status);
            Assert.DoesNotContain("já pode ser removida", saude.Description);
            Assert.Equal(pendentesAntes, await PendentesAsync(conexao, Rotacao));   // nada meio gravado

            await using (var db = SeedIntegracao.Contexto(conexao))
                await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER falhar ON "UserIdentities";""");

            await RodarAsync(conexao, Rotacao, estado);
            Assert.True(estado.Ultima!.Concluida);
            Assert.Equal(0, await PendentesAsync(conexao, Rotacao));
        }

        [FactComDocker]
        public async Task Valor_ilegivel_mantem_o_aviso_de_nao_remover_a_chave_anterior()
        {
            var conexao = await CenarioAsync(_pg, Rotacao.Anterior!);
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var u = await db.UserIdentities.FirstAsync(x => x.NomeLegal == "Pessoa 0");
                u.CpfCnpjCriptografado = DataProtector.Encrypt("55566677788",
                    DataProtector.DerivarChave("uma-terceira-chave-que-ninguem-tem-0123"));
                await db.SaveChangesAsync();
            }
            var estado = new EstadoRecifragem();

            await RodarAsync(conexao, Rotacao, estado);

            Assert.Equal(1, estado.Ultima!.Ilegiveis);
            var saude = RecifragemHealthCheck.Avaliar(ligado: true, estado.Ultima);
            Assert.Equal(HealthStatus.Degraded, saude.Status);
            Assert.Contains("NÃO remova", saude.Description);
        }

        // ── Concorrência entre réplicas ──────────────────────────────────────

        private static (RecifragemAutomaticaService Servico, ServiceProvider Provider) Replica(string conexao, EstadoRecifragem estado)
        {
            var servicos = new ServiceCollection();
            servicos.AddDbContext<TratooContext>(o => o.UseNpgsql(conexao));
            var provider = servicos.BuildServiceProvider();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RecifragemAutomaticaService.ChaveLigado] = "true"
            }).Build();
            return (new RecifragemAutomaticaService(provider.GetRequiredService<IServiceScopeFactory>(), config,
                estado, NullLogger<RecifragemAutomaticaService>.Instance), provider);
        }

        [FactComDocker]
        public async Task Com_a_trava_em_outra_replica_a_rodada_e_ignorada_e_nada_muda()
        {
            // A rodada real usa as chaves do processo (a da suíte); o cenário fica nela, em CBC.
            var conexao = await CenarioAsync(_pg, DataProtector.Chaves.Atual);
            var processo = DataProtector.Chaves;
            var pendentesAntes = await PendentesAsync(conexao, processo);
            Assert.True(pendentesAntes > 0);

            var estado = new EstadoRecifragem();
            var (replica, provider) = Replica(conexao, estado);
            await using var _ = provider;

            // "Outra réplica" segurando a trava.
            await using (var outra = SeedIntegracao.Contexto(conexao))
            {
                await using var tx = await outra.Database.BeginTransactionAsync();
                Assert.True(await outra.Database
                    .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({ExecucaoExclusiva.ChaveRecifragem}) AS \"Value\"")
                    .SingleAsync());

                Assert.False(await replica.ExecutarRodadaAsync(CancellationToken.None));
                Assert.Null(estado.Ultima);
                Assert.Equal(pendentesAntes, await PendentesAsync(conexao, processo));
            }

            // Trava liberada: agora roda.
            Assert.True(await replica.ExecutarRodadaAsync(CancellationToken.None));
            Assert.True(estado.Ultima!.Concluida);
            Assert.Equal(0, await PendentesAsync(conexao, processo));
        }

        [FactComDocker]
        public async Task Duas_replicas_ao_mesmo_tempo_convergem_sem_perder_valor()
        {
            var conexao = await CenarioAsync(_pg, DataProtector.Chaves.Atual, linhas: 15);
            var (a, pa) = Replica(conexao, new EstadoRecifragem());
            var (b, pb) = Replica(conexao, new EstadoRecifragem());
            await using var _a = pa;
            await using var _b = pb;

            var executaram = await Task.WhenAll(
                a.ExecutarRodadaAsync(CancellationToken.None), b.ExecutarRodadaAsync(CancellationToken.None));

            Assert.Contains(true, executaram);
            Assert.Equal(0, await PendentesAsync(conexao, DataProtector.Chaves));
            await using var db = SeedIntegracao.Contexto(conexao);
            foreach (var u in await db.UserIdentities.AsNoTracking().Where(u => u.NomeLegal.StartsWith("Pessoa")).ToListAsync())
                Assert.StartsWith("1234567890", DataProtector.Decrypt(u.CpfCnpjCriptografado));
        }
    }

    public class RecifragemHealthCheckTests
    {
        private static ResultadoRecifragem Ok(bool anterior) =>
            new(DateTime.UtcNow, 0, 0, 3, anterior, null);

        [Fact]
        public void Desligada_e_saudavel() =>
            Assert.Equal(HealthStatus.Healthy, RecifragemHealthCheck.Avaliar(false, null).Status);

        [Fact]
        public void Ligada_sem_rodada_e_saudavel_e_nao_libera_a_chave()
        {
            var r = RecifragemHealthCheck.Avaliar(true, null);
            Assert.Equal(HealthStatus.Healthy, r.Status);
            Assert.DoesNotContain("pode ser removida", r.Description);
        }

        [Fact]
        public void Concluida_com_chave_anterior_avisa_que_ela_pode_sair() =>
            Assert.Contains("já pode ser removida", RecifragemHealthCheck.Avaliar(true, Ok(anterior: true)).Description);

        [Fact]
        public void Concluida_sem_chave_anterior_nao_fala_em_remover() =>
            Assert.DoesNotContain("removida", RecifragemHealthCheck.Avaliar(true, Ok(anterior: false)).Description);

        [Theory]
        [InlineData(2, 0, null)]               // pendências depois da rodada
        [InlineData(0, 1, null)]               // ilegíveis
        [InlineData(-1, -1, "NpgsqlException")] // rodada falhou
        public void Qualquer_problema_e_Degraded_e_nunca_libera_a_chave(int pendentes, int ilegiveis, string? erro)
        {
            var r = RecifragemHealthCheck.Avaliar(true, new ResultadoRecifragem(DateTime.UtcNow, pendentes, ilegiveis, 0, true, erro));
            Assert.Equal(HealthStatus.Degraded, r.Status);
            Assert.DoesNotContain("já pode ser removida", r.Description);
            Assert.Contains("NÃO remova", r.Description);
        }
    }
}
