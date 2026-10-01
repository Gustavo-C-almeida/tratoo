using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tratoo.API.BackgroundServices;
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
    /// BackgroundServices com N réplicas, contra Postgres REAL (mesma imagem do compose,
    /// migrations reais). Cada teste simula as réplicas com escopos de DI independentes
    /// — DbContexts e conexões separados, exatamente como dois processos.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class JobsConcorrenciaTests
    {
        private static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);
        private readonly PostgresFixture _pg;

        public JobsConcorrenciaTests(PostgresFixture pg) => _pg = pg;

        // ── Dublês ──────────────────────────────────────────────────────────────

        /// <summary>Gateway Asaas que só conta transferências (e pode segurar a primeira).</summary>
        private sealed class GatewayContador
        {
            private int _transferencias;
            public int Transferencias => Volatile.Read(ref _transferencias);
            public TaskCompletionSource? Segurar { get; init; }
            public TaskCompletionSource Entrou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public IAsaasGatewayService Criar() => Falso<IAsaasGatewayService>.Criar(
                new Dictionary<string, Func<object?[], object?>>
                {
                    [nameof(IAsaasGatewayService.CriarTransferenciaPixAsync)] = _ => TransferirAsync()
                });

            private async Task<AsaasTransferenciaResponse> TransferirAsync()
            {
                Interlocked.Increment(ref _transferencias);
                Entrou.TrySetResult();
                if (Segurar is not null)
                    await Segurar.Task.WaitAsync(Limite);
                return new AsaasTransferenciaResponse("tra_" + Guid.NewGuid().ToString("N")[..10], "PENDING");
            }
        }

        /// <summary>Segura quem chega até as DUAS instâncias chegarem; aí solta as duas juntas.</summary>
        private sealed class BarreiraDupla
        {
            private int _chegaram;
            private readonly TaskCompletionSource _todas = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task ChegarAsync()
            {
                if (Interlocked.Increment(ref _chegaram) >= 2)
                    _todas.TrySetResult();
                await _todas.Task.WaitAsync(Limite); // timeout = a outra instância nunca chegou aqui
            }
        }

        /// <summary>
        /// IPrestadorRepository real com uma barreira em GetCompletoAsync — chamado DEPOIS
        /// da checagem inicial de status e ANTES da reivindicação atômica. Garante que as
        /// duas instâncias leram o pagamento como Retido e disputam a reivindicação de
        /// verdade (em vez de a segunda só encontrar o trabalho já feito).
        /// </summary>
        private sealed class PrestadorRepositoryComBarreira : IPrestadorRepository
        {
            private readonly IPrestadorRepository _real;
            private readonly BarreiraDupla _barreira;

            public PrestadorRepositoryComBarreira(IPrestadorRepository real, BarreiraDupla barreira)
            {
                _real = real;
                _barreira = barreira;
            }

            public Task<Prestador?> GetByIdAsync(int id) => _real.GetByIdAsync(id);
            public Task UpdateAsync(Prestador prestador) => _real.UpdateAsync(prestador);
            public Task SaveAsync() => _real.SaveAsync();

            public async Task<Prestador?> GetCompletoAsync(int id)
            {
                await _barreira.ChegarAsync();
                return await _real.GetCompletoAsync(id);
            }
        }

        private static ServiceProvider Servicos(string conexao, IAsaasGatewayService gateway, BarreiraDupla? barreira = null)
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
            s.AddScoped<IPrestadorRepository>(sp =>
            {
                var real = ActivatorUtilities.CreateInstance<PrestadorRepository>(sp);
                return barreira is null ? real : new PrestadorRepositoryComBarreira(real, barreira);
            });

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

        // ── Pagamento ───────────────────────────────────────────────────────────

        [FactComDocker]
        public async Task Duas_replicas_liberando_o_mesmo_pagamento_geram_uma_unica_transferencia()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            var gateway = new GatewayContador();
            var barreira = new BarreiraDupla();
            await using var servicos = Servicos(conexao, gateway.Criar(), barreira);

            async Task LiberarComoReplicaAsync()
            {
                await using var escopo = servicos.CreateAsyncScope();
                await escopo.ServiceProvider.GetRequiredService<IPagamentoService>()
                    .LiberarAutomaticamenteAsync(cenario.PagamentoId!.Value);
            }

            // As duas passam juntas pela barreira e disputam a reivindicação no banco.
            await Task.WhenAll(LiberarComoReplicaAsync(), LiberarComoReplicaAsync()).WaitAsync(Limite);

            Assert.Equal(1, gateway.Transferencias);

            await using var db = SeedIntegracao.Contexto(conexao);
            var pagamento = await db.Pagamentos.SingleAsync(p => p.Id == cenario.PagamentoId);
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, pagamento.Status);
            Assert.Equal(1, await db.LedgerFinanceiro.CountAsync(l =>
                l.PagamentoId == cenario.PagamentoId && l.Tipo == TipoEntradaLedger.LiberacaoPrestador));
        }

        [FactComDocker]
        public async Task Duas_rodadas_simultaneas_do_job_de_liberacao_so_uma_varre()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);

            // A primeira rodada fica "presa" dentro do gateway, segurando a trava.
            var segurar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gateway = new GatewayContador { Segurar = segurar };
            await using var servicos = Servicos(conexao, gateway.Criar());
            var escopos = servicos.GetRequiredService<IServiceScopeFactory>();

            var replicaA = new PagamentoLiberacaoService(escopos, NullLogger<PagamentoLiberacaoService>.Instance);
            var replicaB = new PagamentoLiberacaoService(escopos, NullLogger<PagamentoLiberacaoService>.Instance);

            var rodadaA = replicaA.ExecutarRodadaAsync(CancellationToken.None);
            await gateway.Entrou.Task.WaitAsync(Limite);      // A está no meio da rodada

            var rodadaB = await replicaB.ExecutarRodadaAsync(CancellationToken.None);
            Assert.False(rodadaB);                              // B viu a trava e não varreu

            segurar.SetResult();
            Assert.True(await rodadaA.WaitAsync(Limite));
            Assert.Equal(1, gateway.Transferencias);

            // A trava foi liberada no fim da rodada: a próxima roda (e não acha nada a fazer).
            Assert.True(await replicaB.ExecutarRodadaAsync(CancellationToken.None));
            Assert.Equal(1, gateway.Transferencias);

            await using var db = SeedIntegracao.Contexto(conexao);
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso,
                (await db.Pagamentos.SingleAsync(p => p.Id == cenario.PagamentoId)).Status);
        }

        [FactComDocker]
        public async Task Trava_de_um_job_nao_bloqueia_outro_job()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await using var servicos = Servicos(conexao, Falso<IAsaasGatewayService>.Criar());
            var escopos = servicos.GetRequiredService<IServiceScopeFactory>();
            var segurar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dentro = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var pagamento = ExecucaoExclusiva.TentarExecutarAsync(escopos, ExecucaoExclusiva.ChavePagamentoLiberacao,
                async (_, _) => { dentro.SetResult(); await segurar.Task; }, NullLogger.Instance, CancellationToken.None);
            await dentro.Task.WaitAsync(Limite);

            var reindexou = await ExecucaoExclusiva.TentarExecutarAsync(escopos, ExecucaoExclusiva.ChaveReindexacao,
                (_, _) => Task.CompletedTask, NullLogger.Instance, CancellationToken.None);
            Assert.True(reindexou);

            segurar.SetResult();
            Assert.True(await pagamento.WaitAsync(Limite));
        }

        // ── Contratos ───────────────────────────────────────────────────────────

        [FactComDocker]
        public async Task Duas_replicas_expirando_contratos_cancelam_cada_contrato_uma_vez()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao,
                statusContrato: ContratoServicoStatus.AguardandoAssinatura,
                contratoExpiraEm: DateTime.UtcNow.AddDays(-1));
            await using var servicos = Servicos(conexao, Falso<IAsaasGatewayService>.Criar());

            async Task<int> ExpirarComoReplicaAsync()
            {
                await using var escopo = servicos.CreateAsyncScope();
                return await escopo.ServiceProvider.GetRequiredService<IContratoServicoService>().ExpirarContratosAsync();
            }

            var totais = await Task.WhenAll(ExpirarComoReplicaAsync(), ExpirarComoReplicaAsync()).WaitAsync(Limite);

            Assert.Equal(1, totais.Sum());

            await using var db = SeedIntegracao.Contexto(conexao);
            var contrato = await db.ContratosServico.SingleAsync(c => c.Id == cenario.ContratoId);
            var proposta = await db.PropostasProjeto.SingleAsync(p => p.Id == cenario.PropostaId);
            var projeto = await db.Projetos.SingleAsync(p => p.Id == cenario.ProjetoId);
            Assert.Equal(ContratoServicoStatus.Cancelado, contrato.Status);
            Assert.Equal(StatusPropostaProjeto.Recusada, proposta.Status);
            Assert.Equal(StatusProjeto.Aberto, projeto.Status);
            Assert.Null(projeto.FreelancerSelecionadoId);
        }

        [FactComDocker]
        public async Task Contrato_assinado_depois_da_leitura_do_job_nao_e_cancelado()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao,
                statusContrato: ContratoServicoStatus.AguardandoAssinatura,
                contratoExpiraEm: DateTime.UtcNow.AddMinutes(-5));

            // O job já leu o contrato como pendente; antes de ele escrever, as partes assinam.
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                await db.ContratosServico.Where(c => c.Id == cenario.ContratoId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContratoServicoStatus.Ativo));
            }

            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var cancelou = await new ContratoServicoRepository(db).CancelarPorExpiracaoAsync(
                    cenario.ContratoId, cenario.PropostaId, cenario.ProjetoId, DateTime.UtcNow, "expirado", "expirado");
                Assert.False(cancelou);
            }

            await using var conferir = SeedIntegracao.Contexto(conexao);
            Assert.Equal(ContratoServicoStatus.Ativo,
                (await conferir.ContratosServico.SingleAsync(c => c.Id == cenario.ContratoId)).Status);
            Assert.Equal(StatusPropostaProjeto.Convertida,
                (await conferir.PropostasProjeto.SingleAsync(p => p.Id == cenario.PropostaId)).Status);
            Assert.Equal(StatusProjeto.EmAndamento,
                (await conferir.Projetos.SingleAsync(p => p.Id == cenario.ProjetoId)).Status);
        }

        // ── Propostas ───────────────────────────────────────────────────────────

        [FactComDocker]
        public async Task Duas_replicas_expirando_propostas_expiram_cada_uma_uma_vez()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            var agora = DateTime.UtcNow;

            Guid vencida1, vencida2, valida;
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                PropostaProjeto Nova(StatusPropostaProjeto status, DateTime validoAte) => new()
                {
                    ProjetoId = cenario.ProjetoId, PrestadorId = cenario.PrestadorId,
                    Status = status, VersaoAtual = 1, ValidoAte = validoAte
                };
                var a = Nova(StatusPropostaProjeto.Submitted, agora.AddDays(-1));
                var b = Nova(StatusPropostaProjeto.EmNegociacao, agora.AddDays(-2));
                var c = Nova(StatusPropostaProjeto.Submitted, agora.AddDays(5));
                db.PropostasProjeto.AddRange(a, b, c);
                await db.SaveChangesAsync();
                (vencida1, vencida2, valida) = (a.Id, b.Id, c.Id);
            }

            async Task<int> ExpirarComoReplicaAsync()
            {
                await using var db = SeedIntegracao.Contexto(conexao);
                return await new PropostaProjetoRepository(db).ExpirarVencidasAsync(DateTime.UtcNow);
            }

            var totais = await Task.WhenAll(ExpirarComoReplicaAsync(), ExpirarComoReplicaAsync()).WaitAsync(Limite);
            Assert.Equal(2, totais.Sum());

            await using var conferir = SeedIntegracao.Contexto(conexao);
            var status = await conferir.PropostasProjeto
                .Where(p => p.Id == vencida1 || p.Id == vencida2 || p.Id == valida)
                .ToDictionaryAsync(p => p.Id, p => p.Status);
            Assert.Equal(StatusPropostaProjeto.Expirada, status[vencida1]);
            Assert.Equal(StatusPropostaProjeto.Expirada, status[vencida2]);
            Assert.Equal(StatusPropostaProjeto.Submitted, status[valida]);
            // A proposta já convertida do cenário não é tocada.
            Assert.Equal(StatusPropostaProjeto.Convertida,
                (await conferir.PropostasProjeto.SingleAsync(p => p.Id == cenario.PropostaId)).Status);
        }

        // ── Avaliações ──────────────────────────────────────────────────────────

        [FactComDocker]
        public async Task Avaliacao_pendente_e_finalizada_uma_unica_vez_e_nunca_contra_o_estado_lido()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var antiga = DateTime.UtcNow.AddDays(-10);

            // Uma avaliação por (contrato, avaliador) — índice único real do modelo.
            async Task<Avaliacao> NovaAsync(int? nota)
            {
                var cenario = await SeedIntegracao.CriarAsync(conexao);
                return new Avaliacao
                {
                    ContratoServicoId = cenario.ContratoId,
                    AvaliadorId = cenario.ContratanteId,
                    AvaliadoId = cenario.PrestadorId,
                    Nota = nota,
                    Status = StatusAvaliacao.Pendente,
                    CriadoEm = antiga
                };
            }

            Guid comNota, semNota, preenchidaDepois;
            var a = await NovaAsync(5);
            var b = await NovaAsync(null);
            var c = await NovaAsync(null);
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                db.Avaliacoes.AddRange(a, b, c);
                await db.SaveChangesAsync();
                (comNota, semNota, preenchidaDepois) = (a.Id, b.Id, c.Id);

                // c: o job a leu sem nota, mas o usuário enviou a nota antes da escrita.
                await db.Avaliacoes.Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Nota, 4));
            }

            async Task<bool> FinalizarAsync(Guid id, bool publicar)
            {
                await using var db = SeedIntegracao.Contexto(conexao);
                return await new AvaliacaoRepository(db).FinalizarPendentePorExpiracaoAsync(id, publicar, DateTime.UtcNow);
            }

            // Duas réplicas finalizando a mesma avaliação: só uma consegue.
            var publicacoes = await Task.WhenAll(FinalizarAsync(comNota, true), FinalizarAsync(comNota, true));
            Assert.Equal(1, publicacoes.Count(x => x));

            Assert.True(await FinalizarAsync(semNota, publicar: false));
            Assert.False(await FinalizarAsync(preenchidaDepois, publicar: false)); // não oculta a nota recém-enviada

            await using var conferir = SeedIntegracao.Contexto(conexao);
            var status = await conferir.Avaliacoes
                .Where(a => a.Id == comNota || a.Id == semNota || a.Id == preenchidaDepois)
                .ToDictionaryAsync(a => a.Id, a => a.Status);
            Assert.Equal(StatusAvaliacao.Publicada, status[comNota]);
            Assert.Equal(StatusAvaliacao.Oculta, status[semNota]);
            Assert.Equal(StatusAvaliacao.Pendente, status[preenchidaDepois]);
        }
    }
}
