
namespace Tratoo.API.BackgroundServices
{
    /// <summary>
    /// Serviço de background que executa a expiração automática de avaliações pendentes.
    /// Roda 1 vez por dia. Após 7 dias sem submissão:
    /// - avaliações preenchidas são publicadas individualmente
    /// - slots vazios são encerrados silenciosamente
    /// </summary>
    public class AvaliacaoExpiracaoService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AvaliacaoExpiracaoService> _logger;
        private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

        public AvaliacaoExpiracaoService(
            IServiceScopeFactory scopeFactory,
            ILogger<AvaliacaoExpiracaoService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AvaliacaoExpiracaoService iniciado.");

            // Aguarda startup da aplicação
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExpirarPendentesAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Erro no AvaliacaoExpiracaoService.");
                }

                await Task.Delay(Intervalo, stoppingToken);
            }
        }

        private async Task ExpirarPendentesAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAvaliacaoService>();

            _logger.LogInformation("Executando expiração de avaliações pendentes em {Hora}.", DateTime.UtcNow);
            await service.ExpirarPendentesAsync();

            // Lembrete D+3: avaliações com 3 dias sem preenchimento (melhoria #4)
            try
            {
                await service.EnviarLembretesD3Async();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha parcial no envio de lembretes D+3.");
            }
        }
    }
}
