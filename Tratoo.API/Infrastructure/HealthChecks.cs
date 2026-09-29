using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tratoo.Domain.Data;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Readiness do banco relacional principal (Neon PostgreSQL, base "tratoo").
    ///
    /// Implementado à mão em vez de <c>AddDbContextCheck</c> de propósito: aquele
    /// método vive no pacote Microsoft.Extensions.Diagnostics.HealthChecks
    /// .EntityFrameworkCore, que NÃO faz parte do shared framework e seria uma
    /// dependência nova — e ele faz exatamente o que está aqui
    /// (<c>CanConnectAsync</c>). O projeto já convive com um conflito de versões
    /// da família EF Core (MSB3277), então evitar mais um pacote dessa família
    /// tem valor concreto.
    /// </summary>
    public sealed class PostgresHealthCheck : IHealthCheck
    {
        private readonly TratooContext _db;

        public PostgresHealthCheck(TratooContext db) => _db = db;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _db.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("PostgreSQL acessível.")
                    : HealthCheckResult.Unhealthy("PostgreSQL não respondeu à verificação de conexão.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Falha ao conectar no PostgreSQL.", ex);
            }
        }
    }

    /// <summary>
    /// Readiness da base vetorial (Neon PostgreSQL, base "tratoo_vector").
    ///
    /// Vai além de "a conexão abre": confirma que a extensão <c>vector</c> está
    /// instalada. As duas bases ficam no mesmo host Neon, então um simples
    /// CanConnect na base vetorial diria pouco além do que o check de PostgreSQL
    /// já diz — e a busca semântica depende da extensão, não só da base.
    /// </summary>
    public sealed class PgVectorHealthCheck : IHealthCheck
    {
        private readonly VectorContext _vectorDb;

        public PgVectorHealthCheck(VectorContext vectorDb) => _vectorDb = vectorDb;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Uma única ida ao banco resolve as duas perguntas: se a query executa,
                // a conexão está de pé; se devolve linha, a extensão existe.
                // O alias "Value" é exigido pelo SqlQueryRaw para tipos escalares.
                var linhas = await _vectorDb.Database
                    .SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM pg_extension WHERE extname = 'vector'")
                    .ToListAsync(cancellationToken);

                return linhas.Count > 0
                    ? HealthCheckResult.Healthy("Base vetorial acessível e extensão pgvector instalada.")
                    : HealthCheckResult.Unhealthy(
                        "Base vetorial acessível, mas a extensão pgvector não está instalada.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Falha ao verificar a base vetorial/pgvector.", ex);
            }
        }
    }
}
