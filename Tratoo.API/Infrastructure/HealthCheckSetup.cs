using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Health checks separados por intenção:
    ///
    ///  • <c>/health/live</c>  (tag "live")  — o processo está de pé e atendendo?
    ///    Não toca em dependência externa. É o que o Docker consulta: se o banco
    ///    cair, reiniciar o container não resolve nada e só piora (perde cache,
    ///    derruba conexões válidas).
    ///
    ///  • <c>/health/ready</c> (tag "ready") — dá para receber tráfego? Verifica
    ///    PostgreSQL e pgvector. É o que a Railway consulta no deploy, para só
    ///    promover a nova versão quando as dependências reais respondem.
    ///
    /// Tudo aqui vem do shared framework (Microsoft.AspNetCore.App) — nenhum
    /// pacote NuGet novo.
    /// </summary>
    public static class HealthCheckSetup
    {
        public const string TagLive  = "live";
        public const string TagReady = "ready";

        public const string RotaLive  = "/health/live";
        public const string RotaReady = "/health/ready";

        /// <summary>Prefixo usado pela guarda de onboarding para isentar as sondas.</summary>
        public const string PrefixoRotas = "/health/";

        /// <summary>
        /// Teto por check. Sem isto, um banco que aceita a conexão TCP mas nunca
        /// responde deixaria /health/ready pendurado até o timeout da plataforma.
        /// </summary>
        private static readonly TimeSpan TimeoutPorCheck = TimeSpan.FromSeconds(5);

        public static IServiceCollection AddTratooHealthChecks(this IServiceCollection services)
        {
            services.AddHealthChecks()
                .AddCheck(
                    "self",
                    () => HealthCheckResult.Healthy("Processo atendendo requisições."),
                    tags: new[] { TagLive })
                .AddCheck<PostgresHealthCheck>(
                    "postgres",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { TagReady },
                    timeout: TimeoutPorCheck)
                .AddCheck<PgVectorHealthCheck>(
                    "pgvector",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { TagReady },
                    timeout: TimeoutPorCheck);

            return services;
        }

        /// <summary>
        /// Mapeia as duas rotas. Anônimas por construção — nenhuma exige
        /// autorização — e <c>AllowAnonymous()</c> fica explícito para que uma
        /// futura política global de fallback não as capture sem aviso.
        /// </summary>
        public static WebApplication MapTratooHealthChecks(this WebApplication app)
        {
            app.MapHealthChecks(RotaLive, new HealthCheckOptions
            {
                Predicate = registro => registro.Tags.Contains(TagLive),
                ResponseWriter = EscreverRespostaAsync
            }).AllowAnonymous();

            app.MapHealthChecks(RotaReady, new HealthCheckOptions
            {
                Predicate = registro => registro.Tags.Contains(TagReady),
                ResponseWriter = EscreverRespostaAsync
            }).AllowAnonymous();

            return app;
        }

        /// <summary>
        /// O writer padrão devolve só "Healthy"/"Unhealthy". Quando um deploy da
        /// Railway falha no healthcheck, o que se precisa saber é QUAL dependência
        /// caiu — por isso o corpo detalha check por check. Mensagens de exceção
        /// NÃO entram no corpo (evita vazar host/credencial do banco numa rota
        /// anônima); o detalhe completo fica no log da aplicação.
        /// </summary>
        private static async Task EscreverRespostaAsync(HttpContext context, HealthReport relatorio)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = new
            {
                status = relatorio.Status.ToString(),
                duracaoMs = (int)relatorio.TotalDuration.TotalMilliseconds,
                checks = relatorio.Entries.Select(entrada => new
                {
                    nome = entrada.Key,
                    status = entrada.Value.Status.ToString(),
                    descricao = entrada.Value.Description,
                    duracaoMs = (int)entrada.Value.Duration.TotalMilliseconds
                })
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(payload), context.RequestAborted);
        }
    }
}
