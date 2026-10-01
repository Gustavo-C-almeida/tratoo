using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Tratoo.Domain.Features.Infrastructure;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Escolhe onde vive o estado efêmero compartilhado (OTP, tentativas, cadastro
    /// pendente, marcas de envio) e o contador do rate limiting:
    ///
    ///  • SEM <c>Redis:ConnectionString</c> (produção hoje, 1 réplica): memória do processo —
    ///    exatamente o comportamento anterior. Nada novo é exigido.
    ///  • COM <c>Redis:ConnectionString</c> (compose com 2 réplicas): Redis, protegido por
    ///    <c>Redis:ChaveProtecao</c> (obrigatória; a subida falha sem ela), com health check
    ///    de readiness.
    /// </summary>
    public static class EstadoEfemeroSetup
    {
        public const string ChaveConexao = "Redis:ConnectionString";

        public static bool RedisConfigurado(IConfiguration configuration) =>
            !string.IsNullOrWhiteSpace(configuration[ChaveConexao]);

        public static IServiceCollection AddTratooEstadoEfemero(
            this IServiceCollection services, IConfiguration configuration)
        {
            if (!RedisConfigurado(configuration))
                return services.AddTratooEstadoEfemeroEmMemoria();

            // Valida o segredo AGORA (subida), não na primeira requisição com OTP.
            services.AddSingleton(ProtecaoEstadoEfemero.DeConfiguracao(configuration));

            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var opcoes = ConfigurationOptions.Parse(configuration[ChaveConexao]!);
                // Quedas e reconexões da conexão com o Redis aparecem no log da API.
                opcoes.LoggerFactory = sp.GetRequiredService<ILoggerFactory>();
                // Sobe mesmo com o Redis fora do ar (reconecta sozinho) — o processo não
                // pode depender do Redis para nascer; o /health/ready é quem reporta.
                opcoes.AbortOnConnectFail = false;
                // Sem fila de espera enquanto desconectado: falha na hora. Com a fila
                // padrão, cada login esperaria o timeout inteiro antes de o rate limit
                // degradar e cada OTP antes do 503.
                opcoes.BacklogPolicy = BacklogPolicy.FailFast;
                return ConnectionMultiplexer.Connect(opcoes);
            });

            services.AddSingleton<IEstadoEfemero, EstadoEfemeroRedis>();

            services.AddHealthChecks()
                .AddCheck<RedisHealthCheck>(
                    "redis",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { HealthCheckSetup.TagReady },
                    timeout: TimeSpan.FromSeconds(5));

            return services;
        }

        /// <summary>Estado em memória (1 réplica). Público para os testes.</summary>
        public static IServiceCollection AddTratooEstadoEfemeroEmMemoria(this IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddSingleton<IEstadoEfemero, EstadoEfemeroEmMemoria>();
            return services;
        }
    }
}
