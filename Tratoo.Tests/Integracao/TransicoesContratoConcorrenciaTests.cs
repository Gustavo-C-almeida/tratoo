using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Features.Contratos;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Achado D3 (M4) — as transições do contrato sob concorrência, de forma DETERMINÍSTICA.
    /// Os testes HTTP de EntregaFluxoTests disparam requisições em paralelo e dependem de elas
    /// se sobreporem; aqui a sobreposição é garantida: a operação A grava sua transição e segura
    /// a transação, a B é disparada sobre a mesma linha e o teste espera o Postgres mostrá-la
    /// bloqueada (pg_stat_activity) antes de A confirmar. B sempre decide vendo A já gravada —
    /// o pior caso de uma corrida entre réplicas, reproduzido toda vez.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class TransicoesContratoConcorrenciaTests
    {
        private readonly PostgresFixture _pg;
        public TransicoesContratoConcorrenciaTests(PostgresFixture pg) => _pg = pg;

        private async Task<(string Conexao, Guid Contrato)> ContratoAsync(ContratoServicoStatus status, bool comEntregaRegistrada = false)
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            await using var db = SeedIntegracao.Contexto(conexao);
            await db.ContratosServico.Where(c => c.Id == cenario.ContratoId).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.EntregaRegistradaEm, comEntregaRegistrada ? DateTime.UtcNow : (DateTime?)null));
            return (conexao, cenario.ContratoId);
        }

        private static async Task<ContratoServicoStatus> StatusAsync(string conexao, Guid contrato)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            return (await db.ContratosServico.AsNoTracking().SingleAsync(c => c.Id == contrato)).Status;
        }

        private static Func<TratooContext, Task<bool>> Registrar(Guid id) =>
            ctx => new ContratoServicoRepository(ctx).TentarRegistrarEntregaAsync(id, DateTime.UtcNow);
        private static Func<TratooContext, Task<bool>> Aprovar(Guid id) =>
            ctx => new ContratoServicoRepository(ctx).TentarAprovarEntregaAsync(id);
        private static Func<TratooContext, Task<bool>> Rejeitar(Guid id) =>
            ctx => new ContratoServicoRepository(ctx).TentarRejeitarEntregaAsync(id);

        [FactComDocker]
        public async Task Duas_entregas_ao_mesmo_tempo_so_uma_registra()
        {
            var (conexao, id) = await ContratoAsync(ContratoServicoStatus.Ativo);

            var (a, b) = await EsperaDeLock.SerializarAsync(conexao, Registrar(id), Registrar(id));

            Assert.True(a);
            Assert.False(b);
            Assert.Equal(ContratoServicoStatus.AguardandoAprovacaoEntrega, await StatusAsync(conexao, id));
        }

        [FactComDocker]
        public async Task Duas_aprovacoes_ao_mesmo_tempo_so_uma_aprova()
        {
            var (conexao, id) = await ContratoAsync(ContratoServicoStatus.AguardandoAprovacaoEntrega, comEntregaRegistrada: true);

            var (a, b) = await EsperaDeLock.SerializarAsync(conexao, Aprovar(id), Aprovar(id));

            Assert.True(a);
            Assert.False(b);
            Assert.Equal(ContratoServicoStatus.Encerrado, await StatusAsync(conexao, id));
        }

        [TheoryComDocker]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Aprovar_e_rejeitar_ao_mesmo_tempo_so_um_vence(bool aprovarPrimeiro)
        {
            var (conexao, id) = await ContratoAsync(ContratoServicoStatus.AguardandoAprovacaoEntrega, comEntregaRegistrada: true);

            var (a, b) = aprovarPrimeiro
                ? await EsperaDeLock.SerializarAsync(conexao, Aprovar(id), Rejeitar(id))
                : await EsperaDeLock.SerializarAsync(conexao, Rejeitar(id), Aprovar(id));

            Assert.True(a);
            Assert.False(b);
            Assert.Equal(aprovarPrimeiro ? ContratoServicoStatus.Encerrado : ContratoServicoStatus.Ativo,
                await StatusAsync(conexao, id));
        }

        /// <summary>
        /// O cancelamento trava o contrato (SELECT ... FOR UPDATE) e o cancela; o registro de
        /// entrega disparado no meio espera e, depois, não acha mais o contrato Ativo.
        /// </summary>
        [FactComDocker]
        public async Task Cancelamento_em_andamento_faz_o_registro_de_entrega_esperar_e_desistir()
        {
            var (conexao, id) = await ContratoAsync(ContratoServicoStatus.Ativo);

            var (_, registrou) = await EsperaDeLock.SerializarAsync(conexao,
                async ctx =>
                {
                    var repo = new ContratoServicoRepository(ctx);
                    await repo.BloquearParaAtualizacaoAsync(id);
                    await ctx.ContratosServico.Where(c => c.Id == id)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContratoServicoStatus.Cancelado));
                    return true;
                },
                Registrar(id));

            Assert.False(registrou);
            Assert.Equal(ContratoServicoStatus.Cancelado, await StatusAsync(conexao, id));
        }

        /// <summary>
        /// O registro de entrega grava primeiro; o cancelamento disparado no meio espera o lock e,
        /// ao obtê-lo, já enxerga a entrega registrada — e por isso recusa (Situação 3).
        /// </summary>
        [FactComDocker]
        public async Task Registro_em_andamento_faz_o_cancelamento_esperar_e_ver_a_entrega()
        {
            var (conexao, id) = await ContratoAsync(ContratoServicoStatus.Ativo);

            var (registrou, entregaVistaPeloCancelamento) = await EsperaDeLock.SerializarAsync(conexao,
                Registrar(id),
                async ctx =>
                {
                    await using var tx = await ctx.Database.BeginTransactionAsync();
                    await new ContratoServicoRepository(ctx).BloquearParaAtualizacaoAsync(id);
                    var contrato = await ctx.ContratosServico.AsNoTracking().SingleAsync(c => c.Id == id);
                    await tx.CommitAsync();
                    return contrato.EntregaRegistradaEm.HasValue
                        && contrato.Status == ContratoServicoStatus.AguardandoAprovacaoEntrega;
                });

            Assert.True(registrou);
            Assert.True(entregaVistaPeloCancelamento);
        }
    }
}
