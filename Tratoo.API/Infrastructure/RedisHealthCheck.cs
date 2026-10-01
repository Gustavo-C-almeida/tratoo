using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Readiness do Redis — registrado SÓ quando o Redis está configurado (ver
    /// <see cref="EstadoEfemeroSetup"/>). Com Redis, ele é dependência real: sem ele, login
    /// com MFA, cadastro, reset de senha, assinatura de contrato e dados bancários
    /// respondem 503. Mesma política do banco no railway.toml: melhor não promover uma
    /// versão que não fala com as dependências do que promovê-la quebrada.
    /// Liveness (/health/live) continua sem dependências externas.
    /// </summary>
    public sealed class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _redis;

        public RedisHealthCheck(IConnectionMultiplexer redis) => _redis = redis;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var latencia = await _redis.GetDatabase().PingAsync();
                return HealthCheckResult.Healthy($"Redis acessível ({latencia.TotalMilliseconds:F0} ms).");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis indisponível (OTP/tentativas/rate limit compartilhados).", ex);
            }
        }
    }
}
