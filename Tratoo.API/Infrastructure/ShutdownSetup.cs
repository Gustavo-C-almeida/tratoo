using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Encerramento gracioso alinhado ao orçamento que a Railway concede.
    ///
    /// O que já vem do .NET 8 e NÃO é reimplementado aqui: o ConsoleLifetime trata
    /// SIGTERM (o processo é PID 1 via ENTRYPOINT em forma exec, sem shell no meio),
    /// o Kestrel para de aceitar conexões e espera as requisições em andamento, os
    /// BackgroundServices recebem o stoppingToken e o container de DI descarta
    /// DbContexts/pools do Npgsql e faz o flush do Serilog.
    ///
    /// O que faltava:
    ///  • Prazo coerente. Por padrão a Railway dá 0s entre SIGTERM e SIGKILL, e o
    ///    .NET espera até 30s — ou seja, o SIGKILL chegava no meio do encerramento.
    ///    Agora a Railway dá <c>drainingSeconds = 30</c> (railway.toml) e o host usa
    ///    <see cref="TimeoutEncerramento"/> = 25s: estoura antes do SIGKILL, sobrando
    ///    margem para o descarte de recursos e o flush de log depois dele. Um teste
    ///    (ShutdownTests) quebra se os dois valores deixarem de ser coerentes.
    ///  • Evidência. O framework só registra "Application is shutting down...";
    ///    sem registro do fim, SIGKILL e encerramento limpo ficam indistinguíveis.
    /// </summary>
    public static class ShutdownSetup
    {
        /// <summary>
        /// Teto para parar o host (Kestrel drenando requisições + StopAsync dos
        /// hosted services). Precisa ficar abaixo do drainingSeconds do railway.toml.
        /// </summary>
        public static readonly TimeSpan TimeoutEncerramento = TimeSpan.FromSeconds(25);

        public static IServiceCollection AddTratooShutdown(this IServiceCollection services) =>
            services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeoutEncerramento);

        /// <summary>
        /// Registra o ciclo de encerramento para que ele seja observável nos logs:
        ///
        ///   SIGTERM recebido                     ← este handler (sinal do SO)
        ///   Application is shutting down...      ← framework (ApplicationStopping)
        ///   Encerramento concluído em N ms       ← ApplicationStopped: Kestrel e
        ///                                          hosted services já pararam
        ///
        /// Depois disso o Main retorna 0 e o processo sai. Se a última linha não
        /// aparecer, o processo foi morto (SIGKILL) antes de terminar.
        /// </summary>
        public static WebApplication UseLogDeEncerramento(this WebApplication app)
        {
            var lifetime = app.Lifetime;
            var logger = app.Services.GetRequiredService<ILoggerFactory>()
                                     .CreateLogger("Tratoo.Shutdown");
            var cronometro = new Stopwatch();

            // Só observa: não altera context.Cancel, então o tratamento padrão do
            // ConsoleLifetime (que dispara o StopApplication) segue intacto.
            PosixSignalRegistration? registroSigterm = null;
            try
            {
                registroSigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ =>
                    logger.LogInformation(
                        "SIGTERM recebido — iniciando encerramento gracioso (limite {Limite}s).",
                        TimeoutEncerramento.TotalSeconds));
            }
            catch (PlatformNotSupportedException)
            {
                // Plataforma sem SIGTERM: o encerramento continua igual, só sem esta linha.
            }

            lifetime.ApplicationStopping.Register(() => cronometro.Start());

            lifetime.ApplicationStopped.Register(() =>
            {
                registroSigterm?.Dispose();
                logger.LogInformation(
                    "Encerramento concluído em {Duracao} ms: requisições drenadas e serviços parados. Processo saindo.",
                    cronometro.ElapsedMilliseconds);
            });

            return app;
        }
    }
}
