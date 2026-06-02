
namespace Tratoo.API.BackgroundServices
{
    /// <summary>
    /// Serviço de background que verifica pagamentos em escrow com prazo de liberação automática vencido
    /// e dispara a transferência PIX ao prestador sem necessidade de ação do contratante.
    ///
    /// Regra: se o contratante não aprovar a entrega em até 7 dias após o prazo de entrega do contrato,
    /// o sistema libera automaticamente o valor ao prestador.
    ///
    /// Roda a cada 4 horas para minimizar delay sem sobrecarregar o gateway.
    /// </summary>
    public class PagamentoLiberacaoService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PagamentoLiberacaoService> _logger;
        private static readonly TimeSpan Intervalo = TimeSpan.FromHours(4);

        public PagamentoLiberacaoService(
            IServiceScopeFactory scopeFactory,
            ILogger<PagamentoLiberacaoService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PagamentoLiberacaoService iniciado.");

            // Aguarda 2 minutos na inicialização para evitar conflito com outras migrations/seeds
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await LiberarPagamentosVencidosAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Erro no PagamentoLiberacaoService durante varredura.");
                }

                await Task.Delay(Intervalo, stoppingToken);
            }
        }

        private async Task LiberarPagamentosVencidosAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var repo = scope.ServiceProvider.GetRequiredService<IPagamentoRepository>();
            var service = scope.ServiceProvider.GetRequiredService<IPagamentoService>();

            var pagamentosPendentes = await repo.GetPendentesLiberacaoAsync(DateTime.UtcNow);

            if (pagamentosPendentes.Count == 0)
            {
                _logger.LogDebug("Nenhum pagamento pendente de liberação automática em {Hora}.", DateTime.UtcNow);
                return;
            }

            _logger.LogInformation("{Count} pagamento(s) com prazo vencido. Iniciando liberações automáticas.", pagamentosPendentes.Count);

            foreach (var pagamento in pagamentosPendentes)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    _logger.LogInformation(
                        "Liberação automática: pagamento {PagamentoId}, prazo era {Prazo}.",
                        pagamento.Id, pagamento.LiberacaoAutomaticaEm);

                    await service.LiberarAutomaticamenteAsync(pagamento.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Falha na liberação automática do pagamento {PagamentoId}. Será tentado na próxima varredura.",
                        pagamento.Id);
                }
            }

            _logger.LogInformation("Varredura de liberação automática concluída em {Hora}.", DateTime.UtcNow);
        }
    }
}
