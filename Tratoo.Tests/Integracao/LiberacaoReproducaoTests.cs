using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tratoo.API.BackgroundServices;
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
using Tratoo.Domain.Features.Storage;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Achado 08 (liberação PIX em duplicidade), contra Postgres REAL.
    ///
    /// O cenário de duas réplicas já era protegido (trava da rodada + reivindicação
    /// atômica) e continua coberto aqui. O que reproduzia de fato — com UMA instância — era
    /// o timeout ambíguo: o pagamento voltava para Retido e a varredura seguinte criava uma
    /// segunda transferência. Estes testes falhavam antes da correção.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class LiberacaoReproducaoTests
    {
        private static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);
        private readonly PostgresFixture _pg;
        public LiberacaoReproducaoTests(PostgresFixture pg) => _pg = pg;

        /// <summary>
        /// Fake do Asaas que conta pedidos de transferência. <see cref="Responder"/> decide,
        /// pelo número da chamada (1, 2, ...), o que o "Asaas" devolve; por padrão, sucesso.
        /// O pedido é contado ANTES da resposta: num timeout, ele chegou ao Asaas.
        /// </summary>
        private sealed class AsaasContador
        {
            private int _chamadas;
            public int Chamadas => Volatile.Read(ref _chamadas);
            public Func<int, Exception?> Responder { get; init; } = _ => null;
            public ConcurrentQueue<AsaasTransferenciaPixRequest> Pedidos { get; } = new();

            public IAsaasGatewayService Criar() => Falso<IAsaasGatewayService>.Criar(
                new Dictionary<string, Func<object?[], object?>>
                {
                    [nameof(IAsaasGatewayService.CriarTransferenciaPixAsync)] = args =>
                    {
                        Pedidos.Enqueue((AsaasTransferenciaPixRequest)args[0]!);
                        var erro = Responder(Interlocked.Increment(ref _chamadas));
                        return erro is null
                            ? Task.FromResult(new AsaasTransferenciaResponse("tra_" + Guid.NewGuid().ToString("N")[..10], "PENDING"))
                            : Task.FromException<AsaasTransferenciaResponse>(erro);
                    }
                });
        }

        // O que o AsaasGatewayService lança em cada situação.
        private static Exception Timeout() => new AsaasIntegracaoException(HttpStatusCode.ServiceUnavailable,
            "The request was canceled due to the configured HttpClient.Timeout", AsaasErroTipo.ResultadoIncerto,
            "Falha de rede/timeout ao contactar o Asaas");

        private static Exception RejeicaoComprovada() => new AsaasIntegracaoException(HttpStatusCode.BadRequest,
            "{\"errors\":[{\"code\":\"invalid_description\",\"description\":\"A descrição excede o limite\"}]}",
            AsaasErroTipo.PayloadInvalido, "Asaas [400]: A descrição excede o limite");

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

        private static PagamentoLiberacaoService Job(ServiceProvider servicos) =>
            new(servicos.GetRequiredService<IServiceScopeFactory>(), NullLogger<PagamentoLiberacaoService>.Instance);

        /// <summary>Duas varreduras em sequência — a das 00h e a das 04h — na MESMA instância.</summary>
        private static async Task DuasVarredurasAsync(PagamentoLiberacaoService job)
        {
            Assert.True(await job.ExecutarRodadaAsync(CancellationToken.None));
            Assert.True(await job.ExecutarRodadaAsync(CancellationToken.None));
        }

        private static async Task<(StatusPagamento Status, string? StatusGateway, List<string> Ledger)> EstadoAsync(
            string conexao, Guid pagamentoId)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            var p = await db.Pagamentos.SingleAsync(x => x.Id == pagamentoId);
            var ledger = await db.LedgerFinanceiro.Where(l => l.PagamentoId == pagamentoId)
                .Select(l => l.Descricao).ToListAsync();
            return (p.Status, p.StatusGateway, ledger);
        }

        [FactComDocker]
        public async Task Duas_varreduras_concorrentes_chamam_o_Asaas_uma_unica_vez()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            var asaas = new AsaasContador();
            await using var servicos = Servicos(conexao, asaas.Criar());

            await Task.WhenAll(Job(servicos).ExecutarRodadaAsync(CancellationToken.None),
                               Job(servicos).ExecutarRodadaAsync(CancellationToken.None)).WaitAsync(Limite);

            Assert.Equal(1, asaas.Chamadas);
        }

        [FactComDocker]
        public async Task Timeout_ambiguo_nao_gera_segunda_transferencia_e_fica_aguardando_conciliacao()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            var asaas = new AsaasContador { Responder = n => n == 1 ? Timeout() : null };
            await using var servicos = Servicos(conexao, asaas.Criar());

            await DuasVarredurasAsync(Job(servicos));

            Assert.True(asaas.Chamadas == 1,
                $"Asaas recebeu {asaas.Chamadas} pedidos de transferência para o mesmo pagamento.");
            var (status, statusGateway, ledger) = await EstadoAsync(conexao, cenario.PagamentoId!.Value);
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, status);
            Assert.Equal(PagamentoService.MarcadorResultadoIncerto, statusGateway);
            Assert.Single(ledger, d => d.StartsWith("[RESULTADO INCERTO]"));
        }

        [FactComDocker]
        public async Task Excecao_inesperada_na_chamada_tambem_e_resultado_incerto()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            var asaas = new AsaasContador
            {
                Responder = n => n == 1 ? new InvalidOperationException("falha de serialização") : null
            };
            await using var servicos = Servicos(conexao, asaas.Criar());

            await DuasVarredurasAsync(Job(servicos));

            Assert.Equal(1, asaas.Chamadas);
            var (status, statusGateway, _) = await EstadoAsync(conexao, cenario.PagamentoId!.Value);
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, status);
            Assert.Equal(PagamentoService.MarcadorResultadoIncerto, statusGateway);
        }

        [FactComDocker]
        public async Task Rejeicao_comprovada_pelo_Asaas_volta_para_Retido_e_a_proxima_varredura_tenta_de_novo()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            var asaas = new AsaasContador { Responder = n => n == 1 ? RejeicaoComprovada() : null };
            await using var servicos = Servicos(conexao, asaas.Criar());
            var job = Job(servicos);

            Assert.True(await job.ExecutarRodadaAsync(CancellationToken.None));
            var (status, statusGateway, _) = await EstadoAsync(conexao, cenario.PagamentoId!.Value);
            Assert.Equal(StatusPagamento.Retido, status);   // nada foi criado no Asaas
            Assert.NotEqual(PagamentoService.MarcadorResultadoIncerto, statusGateway);

            Assert.True(await job.ExecutarRodadaAsync(CancellationToken.None));
            Assert.Equal(2, asaas.Chamadas);                 // segunda tentativa é legítima
            Assert.Equal(StatusPagamento.TransferenciaEmProgresso, (await EstadoAsync(conexao, cenario.PagamentoId!.Value)).Status);
        }

        [FactComDocker]
        public async Task Liberacao_envia_o_id_do_pagamento_como_referencia_externa()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            var asaas = new AsaasContador();
            await using var servicos = Servicos(conexao, asaas.Criar());

            await Job(servicos).ExecutarRodadaAsync(CancellationToken.None);

            var pedido = Assert.Single(asaas.Pedidos);
            Assert.Equal(cenario.PagamentoId!.Value.ToString(), pedido.ReferenciaExterna);
        }

        [FactComDocker]
        public async Task Reprocessamento_admin_nao_aceita_pagamento_com_resultado_incerto()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao, comPagamentoRetidoVencido: true);
            var asaas = new AsaasContador { Responder = n => n == 1 ? Timeout() : null };
            await using var servicos = Servicos(conexao, asaas.Criar());
            await Job(servicos).ExecutarRodadaAsync(CancellationToken.None);

            await using var escopo = servicos.CreateAsyncScope();
            await Assert.ThrowsAsync<NegocioException>(() => escopo.ServiceProvider
                .GetRequiredService<IPagamentoService>().ReprocessarTransferenciaAsync(cenario.PagamentoId!.Value, adminId: 1));

            Assert.Equal(1, asaas.Chamadas);
        }
    }
}
