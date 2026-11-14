using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Data;
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
using Tratoo.Domain.Models.Prestador;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// A aprovação da entrega dispara a liberação imediata do pagamento; o job de liberação
    /// automática pode reivindicar o MESMO pagamento no meio do caminho. Quando o job vence, a
    /// liberação aconteceu (por ele): o histórico do contrato registra um evento informativo
    /// (LiberacaoProcessadaPorOutraOperacao) — nunca "falha na liberação", nem "pagamento liberado
    /// após a aprovação" (que esta chamada não fez). Sincronização determinística: a aprovação para
    /// depois de ler o pagamento como Retido e antes da reivindicação; o job roda inteiro; só então
    /// a aprovação continua.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class AprovacaoLiberacaoConcorrenteTests
    {
        private static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);
        private readonly PostgresFixture _pg;
        public AprovacaoLiberacaoConcorrenteTests(PostgresFixture pg) => _pg = pg;

        /// <summary>IPrestadorRepository real que para em GetCompletoAsync (chamado antes da reivindicação).</summary>
        private sealed class PrestadorRepositoryPausavel : IPrestadorRepository
        {
            private readonly IPrestadorRepository _real;
            private readonly TaskCompletionSource _chegou, _soltar;
            public PrestadorRepositoryPausavel(IPrestadorRepository real, TaskCompletionSource chegou, TaskCompletionSource soltar)
                => (_real, _chegou, _soltar) = (real, chegou, soltar);

            public Task<Prestador?> GetByIdAsync(int id) => _real.GetByIdAsync(id);
            public Task UpdateAsync(Prestador prestador) => _real.UpdateAsync(prestador);
            public Task SaveAsync() => _real.SaveAsync();
            public async Task<Prestador?> GetCompletoAsync(int id)
            {
                _chegou.TrySetResult();
                await _soltar.Task.WaitAsync(Limite);
                return await _real.GetCompletoAsync(id);
            }
        }

        private static ServiceProvider Servicos(string conexao, IAsaasGatewayService gateway,
            (TaskCompletionSource Chegou, TaskCompletionSource Soltar)? pausa = null)
        {
            var s = new ServiceCollection();
            s.AddLogging();
            s.AddDbContext<TratooContext>(o => o.UseNpgsql(conexao));
            s.AddScoped<IPagamentoRepository, PagamentoRepository>();
            s.AddScoped<IContratoServicoRepository, ContratoServicoRepository>();
            s.AddScoped<IEntregaRepository, EntregaRepository>();
            s.AddScoped<IContratanteRepository, ContratanteRepository>();
            s.AddScoped<IIdentidadeRepository, IdentidadeRepository>();
            s.AddScoped<IAuditLogRepository, AuditLogRepository>();
            s.AddScoped<IProjetoRepository, ProjetoRepository>();
            s.AddScoped<IPropostaProjetoRepository, PropostaProjetoRepository>();
            s.AddScoped<IPrestadorRepository>(sp =>
            {
                var real = ActivatorUtilities.CreateInstance<PrestadorRepository>(sp);
                return pausa is { } p ? new PrestadorRepositoryPausavel(real, p.Chegou, p.Soltar) : real;
            });
            s.AddScoped<IPagamentoService, PagamentoService>();
            s.AddScoped<IEntregaService, EntregaService>();
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

        [FactComDocker]
        public async Task Job_libera_durante_a_aprovacao_e_o_historico_registra_evento_informativo_nao_falha()
        {
            var conexao = EsperaDeLock.SemPool(await _pg.CriarBancoMigradoAsync());
            var c = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                await db.ContratosServico.Where(x => x.Id == c.ContratoId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, ContratoServicoStatus.AguardandoAprovacaoEntrega)
                    .SetProperty(x => x.EntregaRegistradaEm, (DateTime?)DateTime.UtcNow));
                db.Entregas.Add(new Entrega
                {
                    ContratoServicoId = c.ContratoId, DescricaoEntrega = "Entrega do teste.",
                    Status = EntregaStatus.PendenteAprovacao, DataEntrega = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var transferencias = 0;
            var gateway = Falso<IAsaasGatewayService>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                [nameof(IAsaasGatewayService.CriarTransferenciaPixAsync)] = _ =>
                {
                    Interlocked.Increment(ref transferencias);
                    return Task.FromResult(new AsaasTransferenciaResponse("tra_" + Guid.NewGuid().ToString("N")[..10], "PENDING"));
                }
            });
            var pausa = (Chegou: new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                         Soltar: new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            await using var aprovacaoSp = Servicos(conexao, gateway, pausa);
            await using var jobSp = Servicos(conexao, gateway);

            // A aprovação grava, lê o pagamento como Retido e para antes de reivindicá-lo.
            await using var escopoAprovacao = aprovacaoSp.CreateAsyncScope();
            var aprovar = escopoAprovacao.ServiceProvider.GetRequiredService<IEntregaService>()
                .AprovarEntregaAsync(c.ContratoId, c.ContratanteId, "127.0.0.1", "teste");
            await pausa.Chegou.Task.WaitAsync(Limite);

            // O job reivindica e transfere.
            await using (var escopoJob = jobSp.CreateAsyncScope())
                await escopoJob.ServiceProvider.GetRequiredService<IPagamentoService>()
                    .LiberarAutomaticamenteAsync(c.PagamentoId!.Value);

            pausa.Soltar.SetResult();
            await aprovar.WaitAsync(Limite);   // a aprovação em si é um sucesso

            await using var conferir = SeedIntegracao.Contexto(conexao);
            var historico = (await conferir.HistoricosContrato.AsNoTracking()
                    .Where(h => h.ContratoServicoId == c.ContratoId).ToListAsync())
                .Select(h => (Acao: h.Acao.ToString(), h.Descricao)).ToList();
            var pagamento = await conferir.Pagamentos.AsNoTracking().SingleAsync(p => p.Id == c.PagamentoId);

            Assert.Equal(1, transferencias);
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, pagamento.Status);
            Assert.Equal(ContratoServicoStatus.Encerrado,
                (await conferir.ContratosServico.AsNoTracking().SingleAsync(x => x.Id == c.ContratoId)).Status);
            Assert.DoesNotContain(historico, h => h.Acao == "FalhaLiberacao");
            Assert.DoesNotContain(historico, h => h.Acao == "PagamentoLiberado");
            var informativo = Assert.Single(historico, h => h.Acao == "LiberacaoProcessadaPorOutraOperacao");
            Assert.Contains("TransferenciaEmProgresso", informativo.Descricao);
        }
    }
}
