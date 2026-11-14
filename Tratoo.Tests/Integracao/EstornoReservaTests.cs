using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Avaliacoes;
using Tratoo.Domain.Features.Contratos;
using Tratoo.Domain.Features.Infrastructure;
using Tratoo.Domain.Features.Pagamentos;
using Tratoo.Domain.Features.Perfis;
using Tratoo.Domain.Features.Projetos;
using Tratoo.Domain.Features.Propostas;
using Tratoo.Domain.Features.Shared;
using Tratoo.Domain.Features.Storage;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Achado A1 — estorno com reserva atômica, contra Postgres REAL. Cada réplica é um escopo
    /// de DI próprio (DbContext e conexão separados). A sincronização é determinística: a
    /// operação que deve chegar primeiro fica PARADA dentro do gateway falso (já com a reserva
    /// ou a reivindicação gravada) enquanto a concorrente roda; nada depende de timing.
    /// Invariante verificada em todos: para um mesmo pagamento, no máximo UMA operação
    /// financeira (estorno ou transferência) chega ao gateway.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class EstornoReservaTests
    {
        private static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);
        private readonly PostgresFixture _pg;

        public EstornoReservaTests(PostgresFixture pg) => _pg = pg;

        // ── Dublês e infraestrutura ─────────────────────────────────────────────

        /// <summary>Gateway Asaas que conta estornos e transferências e pode segurar cada um.</summary>
        private sealed class GatewayFalso
        {
            private int _estornos, _transferencias;
            public int Estornos => Volatile.Read(ref _estornos);
            public int Transferencias => Volatile.Read(ref _transferencias);

            /// <summary>Executado DEPOIS de o estorno "acontecer" no Asaas (ex.: lançar, quebrar o banco).</summary>
            public Func<Task>? DepoisDoEstorno { get; set; }
            /// <summary>Executado no lugar do estorno (ex.: recusa definitiva — nada é estornado).</summary>
            public Func<Task>? NoLugarDoEstorno { get; set; }
            public TaskCompletionSource? SegurarEstorno { get; set; }
            public TaskCompletionSource? SegurarTransferencia { get; set; }
            public TaskCompletionSource EntrouEstorno { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource EntrouTransferencia { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public IAsaasGatewayService Criar() => Falso<IAsaasGatewayService>.Criar(
                new Dictionary<string, Func<object?[], object?>>
                {
                    [nameof(IAsaasGatewayService.EstornarCobrancaAsync)] = _ => EstornarAsync(),
                    [nameof(IAsaasGatewayService.CriarTransferenciaPixAsync)] = _ => TransferirAsync()
                });

            private async Task EstornarAsync()
            {
                EntrouEstorno.TrySetResult();
                if (SegurarEstorno is not null) await SegurarEstorno.Task.WaitAsync(Limite);
                if (NoLugarDoEstorno is not null) { await NoLugarDoEstorno(); return; }
                Interlocked.Increment(ref _estornos);
                if (DepoisDoEstorno is not null) await DepoisDoEstorno();
            }

            private async Task<AsaasTransferenciaResponse> TransferirAsync()
            {
                Interlocked.Increment(ref _transferencias);
                EntrouTransferencia.TrySetResult();
                if (SegurarTransferencia is not null) await SegurarTransferencia.Task.WaitAsync(Limite);
                return new AsaasTransferenciaResponse("tra_" + Guid.NewGuid().ToString("N")[..10], "PENDING");
            }
        }

        private static ServiceProvider Servicos(string conexao, IAsaasGatewayService gateway)
        {
            var s = new ServiceCollection();
            s.AddLogging();
            s.AddDbContext<TratooContext>(o => o.UseNpgsql(conexao));

            s.AddScoped<IPagamentoRepository, PagamentoRepository>();
            s.AddScoped<IContratoServicoRepository, ContratoServicoRepository>();
            s.AddScoped<IContratanteRepository, ContratanteRepository>();
            s.AddScoped<IIdentidadeRepository, IdentidadeRepository>();
            s.AddScoped<IAuditLogRepository, AuditLogRepository>();
            s.AddScoped<IProjetoRepository, ProjetoRepository>();
            s.AddScoped<IPropostaProjetoRepository, PropostaProjetoRepository>();
            s.AddScoped<IPrestadorRepository, PrestadorRepository>();

            s.AddScoped<IPagamentoService, PagamentoService>();
            s.AddScoped<IContratoServicoService, ContratoServicoService>();
            s.Configure<AsaasConfig>(_ => { });
            s.AddMemoryCache();
            s.AddTratooEstadoEfemeroEmMemoria();

            s.AddSingleton(gateway);
            s.AddSingleton(Falso<IEmailService>.Criar());
            s.AddSingleton(Falso<IAvaliacaoService>.Criar());
            s.AddSingleton(Falso<IContratosPdfService>.Criar());
            s.AddSingleton(Falso<IR2PrivateStorageService>.Criar());
            return s.BuildServiceProvider();
        }

        private static async Task ComoReplicaAsync(ServiceProvider sp, Func<IServiceProvider, Task> acao)
        {
            await using var escopo = sp.CreateAsyncScope();
            await acao(escopo.ServiceProvider);
        }

        private static Task CancelarAsync(ServiceProvider sp, SeedIntegracao.Cenario c) => ComoReplicaAsync(sp, s =>
            s.GetRequiredService<IContratoServicoService>().CancelarAsync(c.ContratoId, c.ContratanteId, "teste A1"));

        private static Task JobAsync(ServiceProvider sp, SeedIntegracao.Cenario c) => ComoReplicaAsync(sp, s =>
            s.GetRequiredService<IPagamentoService>().LiberarAutomaticamenteAsync(c.PagamentoId!.Value));

        private static Task LiberarManualAsync(ServiceProvider sp, SeedIntegracao.Cenario c) => ComoReplicaAsync(sp, s =>
            s.GetRequiredService<IPagamentoService>().LiberarPagamentoAsync(c.PagamentoId!.Value, c.ContratanteId, "teste A1", "127.0.0.1", "teste"));

        private static Task SolicitarEstornoAsync(ServiceProvider sp, SeedIntegracao.Cenario c) => ComoReplicaAsync(sp, s =>
            s.GetRequiredService<IPagamentoService>().SolicitarEstornoAsync(c.PagamentoId!.Value, c.ContratanteId));

        private static Task AbrirDisputaAsync(ServiceProvider sp, SeedIntegracao.Cenario c) => ComoReplicaAsync(sp, s =>
            s.GetRequiredService<IPagamentoService>().AbrirDisputaAsync(c.PagamentoId!.Value, c.ContratanteId,
                new AbrirDisputaDto("Disputa aberta durante o estorno do teste A1.", null)));

        private static async Task WebhookEstornoAsync(ServiceProvider sp, string conexao, SeedIntegracao.Cenario c)
        {
            string cobranca;
            await using (var db = SeedIntegracao.Contexto(conexao))
                cobranca = (await db.Pagamentos.SingleAsync(p => p.Id == c.PagamentoId)).GatewayPagamentoId!;
            await ComoReplicaAsync(sp, s => s.GetRequiredService<IPagamentoService>().ProcessarWebhookAsync(
                "PAYMENT_REFUNDED", "{\"event\":\"PAYMENT_REFUNDED\",\"payment\":{\"id\":\"" + cobranca + "\"}}"));
        }

        private sealed record Estado(
            ContratoServicoStatus Contrato, DateTime? CanceladoEm, StatusPagamento Pagamento, string? StatusGateway,
            DateTime? EstornadoEm, List<(TipoEntradaLedger Tipo, decimal Valor, string Descricao)> Ledger, int Disputas);

        private static async Task<Estado> EstadoAsync(string conexao, SeedIntegracao.Cenario c)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            var contrato = await db.ContratosServico.AsNoTracking().SingleAsync(x => x.Id == c.ContratoId);
            var pagamento = await db.Pagamentos.AsNoTracking().SingleAsync(x => x.Id == c.PagamentoId);
            var ledger = (await db.LedgerFinanceiro.AsNoTracking().Where(l => l.PagamentoId == c.PagamentoId)
                    .Select(l => new { l.Tipo, l.Valor, l.Descricao }).ToListAsync())
                .Select(l => (l.Tipo, l.Valor, l.Descricao)).ToList();
            var disputas = await db.DisputasPagamento.CountAsync(d => d.PagamentoId == c.PagamentoId);
            return new Estado(contrato.Status, contrato.CanceladoEm, pagamento.Status, pagamento.StatusGateway,
                pagamento.EstornadoEm, ledger, disputas);
        }

        private static int Entradas(Estado e, TipoEntradaLedger tipo, decimal? valor = null) =>
            e.Ledger.Count(l => l.Tipo == tipo && (valor is null || l.Valor == valor));

        /// <summary>Trigger que faz QUALQUER gravação no ledger falhar (simula o banco caindo).</summary>
        private static async Task QuebrarLedgerAsync(string conexao, bool quebrar)
        {
            await using var cx = new NpgsqlConnection(conexao);
            await cx.OpenAsync();
            await using var cmd = cx.CreateCommand();
            cmd.CommandText = quebrar
                ? "CREATE OR REPLACE FUNCTION teste_falha_ledger() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'falha simulada de persistência'; END $$ LANGUAGE plpgsql;" +
                  "CREATE TRIGGER teste_falha_ledger BEFORE INSERT ON \"LedgerFinanceiro\" FOR EACH ROW EXECUTE FUNCTION teste_falha_ledger();"
                : "DROP TRIGGER IF EXISTS teste_falha_ledger ON \"LedgerFinanceiro\";";
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<SeedIntegracao.Cenario> CenarioAsync(string conexao)
            => await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true); // contrato Ativo, sem entrega, liberação vencida

        // ── Estorno feito no Asaas, gravação falha ──────────────────────────────

        [FactComDocker]
        public async Task Estorno_feito_no_Asaas_com_falha_ao_gravar_deixa_o_pagamento_bloqueado_e_o_webhook_recupera()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso { DepoisDoEstorno = () => QuebrarLedgerAsync(conexao, quebrar: true) };
            await using var sp = Servicos(conexao, gateway.Criar());

            // O cancelamento vale: o dinheiro já voltou ao contratante.
            await CancelarAsync(sp, c);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal(1, gateway.Estornos);
            Assert.Equal(ContratoServicoStatus.Cancelado, e.Contrato);
            Assert.Equal(StatusPagamento.EstornoEmProgresso, e.Pagamento);   // nunca volta a Retido
            Assert.Equal(0, Entradas(e, TipoEntradaLedger.EstornoSolicitado));

            // O banco volta. Nada consegue liberar o pagamento estornado.
            await QuebrarLedgerAsync(conexao, quebrar: false);
            await JobAsync(sp, c);
            await Assert.ThrowsAsync<ConflitoException>(() => LiberarManualAsync(sp, c)); // reembolso em processamento: 409
            await Assert.ThrowsAsync<NegocioException>(() => SolicitarEstornoAsync(sp, c));
            Assert.Equal(0, gateway.Transferencias);
            Assert.Equal(1, gateway.Estornos);
            Assert.Equal(StatusPagamento.EstornoEmProgresso, (await EstadoAsync(conexao, c)).Pagamento);

            // Recuperação: o webhook do Asaas conclui, com uma entrada de ledger.
            await WebhookEstornoAsync(sp, conexao, c);
            e = await EstadoAsync(conexao, c);
            Assert.Equal(StatusPagamento.Estornado, e.Pagamento);
            Assert.NotNull(e.EstornadoEm);
            Assert.Equal(1, Entradas(e, TipoEntradaLedger.EstornoConcluido, -1200m));
            Assert.Equal(ContratoServicoStatus.Cancelado, e.Contrato);
        }

        // ── Cancelamento × job de liberação automática ──────────────────────────

        [FactComDocker]
        public async Task Job_reivindica_primeiro_e_o_cancelamento_e_recusado_sem_estorno()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso { SegurarTransferencia = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var sp = Servicos(conexao, gateway.Criar());

            var job = JobAsync(sp, c);
            await gateway.EntrouTransferencia.Task.WaitAsync(Limite);    // reivindicação já gravada

            await Assert.ThrowsAsync<NegocioException>(() => CancelarAsync(sp, c));
            gateway.SegurarTransferencia.SetResult();
            await job.WaitAsync(Limite);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal((0, 1), (gateway.Estornos, gateway.Transferencias));
            Assert.Equal(ContratoServicoStatus.Ativo, e.Contrato);
            Assert.Null(e.CanceladoEm);
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, e.Pagamento);
        }

        [FactComDocker]
        public async Task Cancelamento_reserva_primeiro_e_o_job_nao_transfere()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso { SegurarEstorno = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var sp = Servicos(conexao, gateway.Criar());

            var cancelar = CancelarAsync(sp, c);
            await gateway.EntrouEstorno.Task.WaitAsync(Limite);          // reserva já gravada, Asaas "em andamento"

            await JobAsync(sp, c);                                         // não acha nada liberável
            gateway.SegurarEstorno.SetResult();
            await cancelar.WaitAsync(Limite);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal((1, 0), (gateway.Estornos, gateway.Transferencias));
            Assert.Equal(ContratoServicoStatus.Cancelado, e.Contrato);
            Assert.Equal(StatusPagamento.Estornado, e.Pagamento);
            Assert.Equal(1, Entradas(e, TipoEntradaLedger.EstornoSolicitado, -1200m));
        }

        // ── Cancelamento × liberação manual ─────────────────────────────────────

        [FactComDocker]
        public async Task Liberacao_manual_primeiro_e_o_cancelamento_e_recusado_sem_estorno()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso { SegurarTransferencia = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var sp = Servicos(conexao, gateway.Criar());

            var liberar = LiberarManualAsync(sp, c);
            await gateway.EntrouTransferencia.Task.WaitAsync(Limite);

            await Assert.ThrowsAsync<NegocioException>(() => CancelarAsync(sp, c));
            gateway.SegurarTransferencia.SetResult();
            await liberar.WaitAsync(Limite);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal((0, 1), (gateway.Estornos, gateway.Transferencias));
            Assert.Equal(ContratoServicoStatus.Ativo, e.Contrato);
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, e.Pagamento);
        }

        [FactComDocker]
        public async Task Cancelamento_reserva_primeiro_e_a_liberacao_manual_e_recusada()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso { SegurarEstorno = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var sp = Servicos(conexao, gateway.Criar());

            var cancelar = CancelarAsync(sp, c);
            await gateway.EntrouEstorno.Task.WaitAsync(Limite);

            await Assert.ThrowsAsync<ConflitoException>(() => LiberarManualAsync(sp, c)); // perdeu a corrida: 409
            gateway.SegurarEstorno.SetResult();
            await cancelar.WaitAsync(Limite);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal((1, 0), (gateway.Estornos, gateway.Transferencias));
            Assert.Equal(ContratoServicoStatus.Cancelado, e.Contrato);
            Assert.Equal(StatusPagamento.Estornado, e.Pagamento);
        }

        // ── Outras operações sobre o mesmo pagamento durante o estorno ──────────

        [FactComDocker]
        public async Task Disputa_aberta_durante_o_estorno_e_recusada_e_nao_sobrescreve_a_reserva()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso { SegurarEstorno = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var sp = Servicos(conexao, gateway.Criar());

            var cancelar = CancelarAsync(sp, c);
            await gateway.EntrouEstorno.Task.WaitAsync(Limite);

            await Assert.ThrowsAsync<NegocioException>(() => AbrirDisputaAsync(sp, c));
            gateway.SegurarEstorno.SetResult();
            await cancelar.WaitAsync(Limite);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal(StatusPagamento.Estornado, e.Pagamento);
            Assert.Equal(0, e.Disputas);
        }

        [FactComDocker]
        public async Task Estorno_solicitado_reserva_o_pagamento_e_o_job_nao_transfere()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso { SegurarEstorno = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var sp = Servicos(conexao, gateway.Criar());

            var solicitar = SolicitarEstornoAsync(sp, c);
            await gateway.EntrouEstorno.Task.WaitAsync(Limite);

            await JobAsync(sp, c);
            gateway.SegurarEstorno.SetResult();
            await solicitar.WaitAsync(Limite);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal((1, 0), (gateway.Estornos, gateway.Transferencias));
            Assert.Equal(StatusPagamento.Estornado, e.Pagamento);
            Assert.Equal(1, Entradas(e, TipoEntradaLedger.EstornoSolicitado, -1200m));
        }

        // ── Falha definitiva × resultado incerto ────────────────────────────────

        [FactComDocker]
        public async Task Falha_definitiva_do_Asaas_desfaz_cancelamento_e_reserva_juntos()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso
            {
                NoLugarDoEstorno = () => throw new AsaasIntegracaoException(HttpStatusCode.BadRequest,
                    "{\"errors\":[{\"code\":\"invalid_action\"}]}", AsaasErroTipo.ClienteError, "Asaas recusou o estorno.")
            };
            await using var sp = Servicos(conexao, gateway.Criar());

            var erro = await Assert.ThrowsAsync<NegocioException>(() => CancelarAsync(sp, c));
            Assert.Contains("Tente novamente", erro.Message);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal(ContratoServicoStatus.Ativo, e.Contrato);
            Assert.Null(e.CanceladoEm);
            Assert.Equal(StatusPagamento.Retido, e.Pagamento);
            Assert.Contains(e.Ledger, l => l.Tipo == TipoEntradaLedger.EstornoSolicitado && l.Valor == 0 && l.Descricao.StartsWith("[ESTORNO RECUSADO]"));
            Assert.Equal(0, Entradas(e, TipoEntradaLedger.EstornoSolicitado, -1200m));

            // Nada foi estornado: o fluxo normal segue (o job libera, uma vez).
            await JobAsync(sp, c);
            Assert.Equal((0, 1), (gateway.Estornos, gateway.Transferencias));
        }

        [FactComDocker]
        public async Task Resultado_incerto_mantem_o_cancelamento_e_bloqueia_o_pagamento_sem_retentar()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso
            {
                DepoisDoEstorno = () => throw new AsaasIntegracaoException(HttpStatusCode.GatewayTimeout, "",
                    AsaasErroTipo.ResultadoIncerto, "sem resposta conclusiva")
            };
            await using var sp = Servicos(conexao, gateway.Criar());

            await CancelarAsync(sp, c);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal(ContratoServicoStatus.Cancelado, e.Contrato);
            Assert.Equal(StatusPagamento.EstornoEmProgresso, e.Pagamento);
            Assert.Equal(PagamentoService.MarcadorEstornoIncerto, e.StatusGateway);
            Assert.Contains(e.Ledger, l => l.Valor == 0 && l.Descricao.StartsWith("[RESULTADO INCERTO]"));

            // Ninguém libera, ninguém estorna de novo.
            await JobAsync(sp, c);
            await Assert.ThrowsAsync<ConflitoException>(() => LiberarManualAsync(sp, c)); // reembolso em processamento: 409
            await Assert.ThrowsAsync<NegocioException>(() => SolicitarEstornoAsync(sp, c));
            await Assert.ThrowsAsync<NegocioException>(() => AbrirDisputaAsync(sp, c));
            Assert.Equal((1, 0), (gateway.Estornos, gateway.Transferencias));

            // A confirmação do gateway resolve.
            await WebhookEstornoAsync(sp, conexao, c);
            e = await EstadoAsync(conexao, c);
            Assert.Equal(StatusPagamento.Estornado, e.Pagamento);
            Assert.Equal(1, Entradas(e, TipoEntradaLedger.EstornoConcluido, -1200m));
        }

        // ── Webhook PAYMENT_REFUNDED ─────────────────────────────────────────────

        [FactComDocker]
        public async Task Webhook_de_estorno_e_idempotente_mesmo_reentregue()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var gateway = new GatewayFalso();
            await using var sp = Servicos(conexao, gateway.Criar());

            await CancelarAsync(sp, c);
            Assert.Equal(StatusPagamento.Estornado, (await EstadoAsync(conexao, c)).Pagamento);

            await WebhookEstornoAsync(sp, conexao, c);
            // Reentrega que escapa da chave de idempotência (ex.: registro do webhook perdido).
            await using (var db = SeedIntegracao.Contexto(conexao))
                await db.WebhookLogs.ExecuteDeleteAsync();
            await WebhookEstornoAsync(sp, conexao, c);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal(StatusPagamento.Estornado, e.Pagamento);
            Assert.Equal(1, Entradas(e, TipoEntradaLedger.EstornoSolicitado, -1200m));
            Assert.Equal(1, Entradas(e, TipoEntradaLedger.EstornoConcluido));
        }

        [TheoryComDocker]
        [InlineData(StatusPagamento.TransferenciaEmProgresso)]
        [InlineData(StatusPagamento.Liberado)]
        public async Task Webhook_de_estorno_nao_sobrescreve_pagamento_que_ja_saiu_ao_prestador(StatusPagamento status)
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            await using (var db = SeedIntegracao.Contexto(conexao))
                await db.Pagamentos.Where(p => p.Id == c.PagamentoId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status));
            await using var sp = Servicos(conexao, new GatewayFalso().Criar());

            await WebhookEstornoAsync(sp, conexao, c);
            await using (var db = SeedIntegracao.Contexto(conexao))
                await db.WebhookLogs.ExecuteDeleteAsync();
            await WebhookEstornoAsync(sp, conexao, c);

            var e = await EstadoAsync(conexao, c);
            Assert.Equal(status, e.Pagamento);          // estado incompatível NÃO é sobrescrito
            Assert.Null(e.EstornadoEm);
            var conflitos = e.Ledger.Where(l => l.Tipo == TipoEntradaLedger.EstornoConcluido).ToList();
            Assert.Single(conflitos);                    // registrado para conciliação, uma vez
            Assert.StartsWith("[CONFLITO", conflitos[0].Descricao);
        }

        // ── Exclusão mútua no próprio banco (sem depender de quem chega primeiro) ─

        /// <summary>
        /// A operação A grava sua transição e segura a transação; a B tenta a dela sobre a MESMA
        /// linha e fica bloqueada (verificado em pg_stat_activity); só então A confirma. B decide
        /// vendo A já gravada e não aplica nada. Vale em qualquer ordem e entre réplicas.
        /// </summary>
        [TheoryComDocker]
        [InlineData("reserva", "transferencia")]
        [InlineData("transferencia", "reserva")]
        [InlineData("reserva", "disputa")]
        [InlineData("disputa", "reserva")]
        [InlineData("reserva", "reserva")]
        public async Task Operacoes_financeiras_sobre_o_mesmo_pagamento_sao_mutuamente_exclusivas(string primeira, string segunda)
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await CenarioAsync(conexao);
            var id = c.PagamentoId!.Value;

            Func<TratooContext, Task<bool>> Operacao(string nome) => ctx => nome switch
            {
                "reserva"       => new PagamentoRepository(ctx).TryReservarEstornoAsync(id),
                "transferencia" => new PagamentoRepository(ctx).TryMarcarTransferenciaEmProgressoAsync(id),
                "disputa"       => new PagamentoRepository(ctx).TryMarcarEmDisputaAsync(id),
                _ => throw new ArgumentOutOfRangeException(nameof(nome))
            };

            var (a, b) = await EsperaDeLock.SerializarAsync(conexao, Operacao(primeira), Operacao(segunda));

            Assert.True(a);
            Assert.False(b);
            var esperado = primeira switch
            {
                "reserva" => StatusPagamento.EstornoEmProgresso,
                "transferencia" => StatusPagamento.TransferenciaEmProgresso,
                _ => StatusPagamento.EmDisputa
            };
            Assert.Equal(esperado, (await EstadoAsync(conexao, c)).Pagamento);
        }
    }
}
