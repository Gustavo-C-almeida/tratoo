
namespace Tratoo.API.BackgroundServices
{
    /// <summary>
    /// Serviço de background que cancela contratos não assinados dentro do prazo (ExpiraEm).
    /// Roda a cada hora, logo após a verificação de propostas.
    /// </summary>
    public class ContratoExpiracaoService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ContratoExpiracaoService> _logger;
        private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

        public ContratoExpiracaoService(
            IServiceScopeFactory scopeFactory,
            ILogger<ContratoExpiracaoService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ContratoExpiracaoService iniciado.");

            // Slight offset so it doesn't run at the same second as PropostaExpiracaoService
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExpirarContratosAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao expirar contratos.");
                }

                await Task.Delay(Intervalo, stoppingToken);
            }
        }

        private async Task ExpirarContratosAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ContratoServicoService>();
            await service.ExpirarContratosAsync();
            _logger.LogInformation("Varredura de expiração de contratos concluída em {Hora}.", DateTime.UtcNow);
        }
    }
}
