using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Avaliacoes;
using Tratoo.Domain.Features.Contratos;
using Tratoo.Domain.Features.Infrastructure;
using Tratoo.Domain.Features.Pagamentos;
using Tratoo.Domain.Features.Perfis;
using Tratoo.Domain.Features.Projetos;
using Tratoo.Domain.Features.Propostas;
using Tratoo.Domain.Features.Storage;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Data;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// A confirmação de um pagamento pode chegar por mais de um caminho ao mesmo tempo:
    /// dois eventos do Asaas, o mesmo evento repetido ou o polling de sincronização. Contra
    /// Postgres REAL, com cada "réplica" num escopo de DI próprio: dinheiro e e-mail só uma vez.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class WebhookConcorrenciaTests
    {
        private static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);
        private readonly PostgresFixture _pg;

        public WebhookConcorrenciaTests(PostgresFixture pg) => _pg = pg;

        // ── Dublês ──────────────────────────────────────────────────────────────

        /// <summary>Solta as duas réplicas juntas, depois que ambas já leram o pagamento.</summary>
        private sealed class BarreiraDupla
        {
            private int _chegaram;
            private readonly TaskCompletionSource _todas = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task ChegarAsync()
            {
                if (Interlocked.Increment(ref _chegaram) >= 2)
                    _todas.TrySetResult();
                await _todas.Task.WaitAsync(Limite);
            }
        }

        /// <summary>Repositório real, com a barreira logo depois da leitura do pagamento.</summary>
        public class RepositorioComBarreira : DispatchProxy
        {
            private IPagamentoRepository _real = null!;
            private Func<Task> _aposLeitura = () => Task.CompletedTask;

            internal static IPagamentoRepository Criar(IPagamentoRepository real, Func<Task> aposLeitura)
            {
                var proxy = Create<IPagamentoRepository, RepositorioComBarreira>();
                var interno = (RepositorioComBarreira)(object)proxy;
                interno._real = real;
                interno._aposLeitura = aposLeitura;
                return proxy;
            }

            protected override object? Invoke(MethodInfo? metodo, object?[]? args)
            {
                var resultado = metodo!.Invoke(_real, args);
                return metodo.Name is nameof(IPagamentoRepository.GetByGatewayIdAsync) or nameof(IPagamentoRepository.GetByIdAsync)
                    ? DepoisDaBarreiraAsync((Task<Pagamento?>)resultado!)
                    : resultado;
            }

            private async Task<Pagamento?> DepoisDaBarreiraAsync(Task<Pagamento?> leitura)
            {
                var pagamento = await leitura;
                await _aposLeitura();
                return pagamento;
            }
        }

        private sealed class Emails
        {
            private int _confirmacoes;
            public int Confirmacoes => Volatile.Read(ref _confirmacoes);

            public IEmailService Criar() => Falso<IEmailService>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                [nameof(IEmailService.EnviarNotificacaoPagamentoConfirmadoAsync)] = _ =>
                {
                    Interlocked.Increment(ref _confirmacoes);
                    return Task.CompletedTask;
                }
            });
        }

        private static ServiceProvider Servicos(string conexao, IEmailService email, BarreiraDupla? barreira = null)
        {
            var s = new ServiceCollection();
            s.AddLogging();
            s.AddDbContext<TratooContext>(o => o.UseNpgsql(conexao));

            s.AddScoped<IPagamentoRepository>(sp =>
            {
                var real = ActivatorUtilities.CreateInstance<PagamentoRepository>(sp);
                return barreira is null ? real : RepositorioComBarreira.Criar(real, barreira.ChegarAsync);
            });
            s.AddScoped<IContratoServicoRepository, ContratoServicoRepository>();
            s.AddScoped<IContratanteRepository, ContratanteRepository>();
            s.AddScoped<IIdentidadeRepository, IdentidadeRepository>();
            s.AddScoped<IAuditLogRepository, AuditLogRepository>();
            s.AddScoped<IProjetoRepository, ProjetoRepository>();
            s.AddScoped<IPropostaProjetoRepository, PropostaProjetoRepository>();
            s.AddScoped<IPrestadorRepository, PrestadorRepository>();

            s.AddScoped<IPagamentoService, PagamentoService>();
            s.Configure<AsaasConfig>(_ => { });
            s.AddMemoryCache();
            s.AddTratooEstadoEfemeroEmMemoria();

            s.AddSingleton(Falso<IAsaasGatewayService>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                [nameof(IAsaasGatewayService.ObterStatusCobrancaAsync)] = _ => Task.FromResult("RECEIVED")
            }));
            s.AddSingleton(email);
            s.AddSingleton(Falso<IAvaliacaoService>.Criar());
            s.AddSingleton(Falso<IContratosPdfService>.Criar());
            s.AddSingleton(Falso<IR2PrivateStorageService>.Criar());

            return s.BuildServiceProvider();
        }

        /// <summary>Pagamento ainda não confirmado, à espera do PIX.</summary>
        private static async Task<(SeedIntegracao.Cenario Cenario, string GatewayId)> PagamentoAguardandoAsync(string conexao)
        {
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            await using var db = SeedIntegracao.Contexto(conexao);
            await db.Pagamentos.Where(p => p.Id == cenario.PagamentoId).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, StatusPagamento.Aguardando)
                .SetProperty(p => p.PagoEm, (DateTime?)null));
            var gatewayId = await db.Pagamentos.Where(p => p.Id == cenario.PagamentoId)
                .Select(p => p.GatewayPagamentoId!).SingleAsync();
            return (cenario, gatewayId);
        }

        private static string Payload(string evento, string gatewayId) =>
            $$$"""{"event":"{{{evento}}}","payment":{"id":"{{{gatewayId}}}"}}""";

        private static async Task WebhookAsync(ServiceProvider servicos, string evento, string gatewayId)
        {
            await using var escopo = servicos.CreateAsyncScope();
            await escopo.ServiceProvider.GetRequiredService<IPagamentoService>()
                .ProcessarWebhookAsync(evento, Payload(evento, gatewayId));
        }

        private static async Task ConferirConfirmadoUmaVezAsync(string conexao, Guid pagamentoId, Emails emails)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            var pagamento = await db.Pagamentos.AsNoTracking().SingleAsync(p => p.Id == pagamentoId);
            Assert.Equal(StatusPagamento.Retido, pagamento.Status);
            Assert.NotNull(pagamento.PagoEm);
            Assert.Equal(1, await db.LedgerFinanceiro.CountAsync(l =>
                l.PagamentoId == pagamentoId && l.Tipo == TipoEntradaLedger.CobrancaPaga));
            Assert.Equal(1, await db.LedgerFinanceiro.CountAsync(l =>
                l.PagamentoId == pagamentoId && l.Tipo == TipoEntradaLedger.EscrowRetido));
            Assert.Equal(1, emails.Confirmacoes);
        }

        // ── Cenários ────────────────────────────────────────────────────────────

        [FactComDocker]
        public async Task Dois_eventos_de_confirmacao_em_paralelo_registram_o_pagamento_uma_vez()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var (cenario, gatewayId) = await PagamentoAguardandoAsync(conexao);
            var emails = new Emails();
            await using var servicos = Servicos(conexao, emails.Criar(), new BarreiraDupla());

            await Task.WhenAll(
                WebhookAsync(servicos, "PAYMENT_CONFIRMED", gatewayId),
                WebhookAsync(servicos, "PAYMENT_RECEIVED", gatewayId)).WaitAsync(Limite);

            await ConferirConfirmadoUmaVezAsync(conexao, cenario.PagamentoId!.Value, emails);
        }

        [FactComDocker]
        public async Task Polling_e_webhook_em_paralelo_registram_o_pagamento_uma_vez()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var (cenario, gatewayId) = await PagamentoAguardandoAsync(conexao);
            var emails = new Emails();
            await using var servicos = Servicos(conexao, emails.Criar(), new BarreiraDupla());

            async Task SincronizarAsync()
            {
                await using var escopo = servicos.CreateAsyncScope();
                var resumo = await escopo.ServiceProvider.GetRequiredService<IPagamentoService>()
                    .SincronizarStatusAsync(cenario.PagamentoId!.Value, cenario.ContratanteId);
                // Quem perdeu a corrida também precisa devolver o estado real.
                Assert.Equal(StatusPagamento.Retido, resumo.Status);
            }

            await Task.WhenAll(
                SincronizarAsync(),
                WebhookAsync(servicos, "PAYMENT_RECEIVED", gatewayId)).WaitAsync(Limite);

            await ConferirConfirmadoUmaVezAsync(conexao, cenario.PagamentoId!.Value, emails);
        }

        [FactComDocker]
        public async Task Mesmo_evento_repetido_em_paralelo_e_processado_uma_vez()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var (cenario, gatewayId) = await PagamentoAguardandoAsync(conexao);
            var emails = new Emails();
            await using var servicos = Servicos(conexao, emails.Criar(), new BarreiraDupla());

            await Task.WhenAll(
                WebhookAsync(servicos, "PAYMENT_RECEIVED", gatewayId),
                WebhookAsync(servicos, "PAYMENT_RECEIVED", gatewayId)).WaitAsync(Limite);

            await ConferirConfirmadoUmaVezAsync(conexao, cenario.PagamentoId!.Value, emails);

            await using var db = SeedIntegracao.Contexto(conexao);
            var log = await db.WebhookLogs.SingleAsync(w => w.AsaasCobrancaId == gatewayId);
            Assert.True(log.ProcessadoComSucesso);
        }

        [FactComDocker]
        public async Task Evento_reenviado_depois_de_uma_falha_e_processado()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var (cenario, gatewayId) = await PagamentoAguardandoAsync(conexao);
            var emails = new Emails();
            await using var servicos = Servicos(conexao, emails.Criar());

            // Tentativa anterior: o evento ficou registrado como falho.
            var chave = $"PAYMENT_RECEIVED_{gatewayId}";
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                db.WebhookLogs.Add(new WebhookLog
                {
                    ChaveIdempotencia = chave,
                    TipoEvento = "PAYMENT_RECEIVED",
                    AsaasCobrancaId = gatewayId,
                    PayloadJson = Payload("PAYMENT_RECEIVED", gatewayId),
                    RecebidoEm = DateTime.UtcNow.AddMinutes(-5),
                    ProcessadoEm = DateTime.UtcNow.AddMinutes(-5),
                    ErroMensagem = "falha simulada"
                });
                await db.SaveChangesAsync();
            }

            await WebhookAsync(servicos, "PAYMENT_RECEIVED", gatewayId);

            await ConferirConfirmadoUmaVezAsync(conexao, cenario.PagamentoId!.Value, emails);

            await using var conferir = SeedIntegracao.Contexto(conexao);
            var log = await conferir.WebhookLogs.SingleAsync(w => w.ChaveIdempotencia == chave);
            Assert.True(log.ProcessadoComSucesso);
            Assert.Null(log.ErroMensagem);
        }

        [FactComDocker]
        public async Task Evento_ja_processado_com_sucesso_nao_e_reprocessado()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var (cenario, gatewayId) = await PagamentoAguardandoAsync(conexao);
            var emails = new Emails();
            await using var servicos = Servicos(conexao, emails.Criar());

            await WebhookAsync(servicos, "PAYMENT_RECEIVED", gatewayId);
            await WebhookAsync(servicos, "PAYMENT_RECEIVED", gatewayId);

            await ConferirConfirmadoUmaVezAsync(conexao, cenario.PagamentoId!.Value, emails);
        }
    }
}
