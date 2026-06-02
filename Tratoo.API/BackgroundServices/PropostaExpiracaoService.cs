
namespace Tratoo.API.BackgroundServices
{
    /// <summary>
    /// Serviço de background que verifica propostas expiradas a cada hora
    /// e atualiza o status para Expirada automaticamente.
    /// </summary>
    public class PropostaExpiracaoService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PropostaExpiracaoService> _logger;
        private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

        public PropostaExpiracaoService(
            IServiceScopeFactory scopeFactory,
            ILogger<PropostaExpiracaoService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PropostaExpiracaoService iniciado.");

            // Aguarda a aplicação iniciar antes da primeira varredura
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExpirarPropostasAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao expirar propostas.");
                }

                await Task.Delay(Intervalo, stoppingToken);
            }
        }

        private async Task ExpirarPropostasAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<PropostaProjetoService>();
            await service.ExpirarPropostasAsync();
            _logger.LogInformation("Varredura de expiração de propostas concluída em {Hora}.", DateTime.UtcNow);
        }
    }
}
