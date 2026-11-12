namespace Tratoo.API.BackgroundServices
{
    /// <summary>
    /// De hora em hora, marca como Expirado o convite pendente vencido: 30 dias após o envio, ou
    /// com o projeto fora de Aberto (ConviteProjeto.PendenteVencido). As leituras e respostas já
    /// tratam o vencido na hora; o job só deixa o status gravado coerente.
    /// </summary>
    public class ConviteExpiracaoService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ConviteExpiracaoService> _logger;

        private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

        public ConviteExpiracaoService(IServiceScopeFactory scopeFactory, ILogger<ConviteExpiracaoService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Aguarda a aplicação iniciar antes da primeira varredura
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<ConviteProjetoService>();
                    // Seguro com N réplicas: a expiração é um UPDATE condicional único.
                    var expirados = await service.ExpirarConvitesAsync();
                    _logger.LogInformation("Varredura de expiração de convites concluída: {Total} expirado(s) por esta réplica.", expirados);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao expirar convites.");
                }

                await Task.Delay(Intervalo, stoppingToken);
            }
        }
    }
}
