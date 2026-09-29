using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Separação liveness/readiness: o que cada rota verifica, o que ela devolve e
    /// quem pode chamar.
    ///
    /// Os checks de banco reais (PostgresHealthCheck/PgVectorHealthCheck) exigiriam
    /// Neon de verdade; aqui entram dublês com as MESMAS tags, de modo que o que se
    /// testa é o que pode quebrar sem banco: filtro por tag, status HTTP, corpo,
    /// acesso anônimo e a independência entre as duas rotas. Os checks em si são
    /// validados contra o banco real via `railway run`.
    /// </summary>
    public class HealthCheckTests
    {
        /// <summary>
        /// Esquema de autenticação que nunca autentica. Existe só para o
        /// AuthorizationMiddleware ter como emitir o challenge (401) da rota de
        /// controle — sem ele o middleware lança em vez de responder.
        /// </summary>
        private sealed class EsquemaQueNuncaAutentica : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public EsquemaQueNuncaAutentica(
                IOptionsMonitor<AuthenticationSchemeOptions> options,
                ILoggerFactory logger,
                System.Text.Encodings.Web.UrlEncoder encoder)
                : base(options, logger, encoder) { }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
                => Task.FromResult(AuthenticateResult.NoResult());
        }

        private sealed class CheckFixo : IHealthCheck
        {
            private readonly HealthCheckResult _resultado;
            public CheckFixo(HealthCheckResult resultado) => _resultado = resultado;

            public Task<HealthCheckResult> CheckHealthAsync(
                HealthCheckContext context, CancellationToken cancellationToken = default)
                => Task.FromResult(_resultado);
        }

        /// <summary>
        /// Sobe um host com o mapeamento REAL (MapTratooHealthChecks) e dublês nas
        /// tags reais. <paramref name="readyFalha"/> simula banco indisponível.
        /// </summary>
        private static WebApplication CriarHost(
            bool readyFalha = false, bool comFallbackQueNega = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            builder.Services.AddHealthChecks()
                .AddCheck("self",
                    () => HealthCheckResult.Healthy("Processo atendendo requisições."),
                    tags: new[] { HealthCheckSetup.TagLive })
                .AddCheck("postgres",
                    new CheckFixo(readyFalha
                        ? HealthCheckResult.Unhealthy("Falha simulada de PostgreSQL.")
                        : HealthCheckResult.Healthy("PostgreSQL acessível.")),
                    tags: new[] { HealthCheckSetup.TagReady })
                .AddCheck("pgvector",
                    new CheckFixo(HealthCheckResult.Healthy("pgvector instalado.")),
                    tags: new[] { HealthCheckSetup.TagReady });

            if (comFallbackQueNega)
            {
                // Política global que nega tudo: só passa quem tem AllowAnonymous.
                builder.Services.AddAuthorization(opcoes =>
                    opcoes.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAssertion(_ => false)
                        .Build());

                builder.Services
                    .AddAuthentication("Stub")
                    .AddScheme<AuthenticationSchemeOptions, EsquemaQueNuncaAutentica>("Stub", null);
            }

            var app = builder.Build();

            if (comFallbackQueNega)
            {
                app.UseAuthentication();
                app.UseAuthorization();
            }

            app.MapTratooHealthChecks();
            app.MapGet("/rota-comum", () => Results.Ok());

            return app;
        }

        // ── /health/live ─────────────────────────────────────────────────────

        [Fact]
        public async Task Live_RetornaOkEVerificaApenasOProcesso()
        {
            await using var app = CriarHost();
            await app.StartAsync();

            var resposta = await app.GetTestClient().GetAsync(HealthCheckSetup.RotaLive);
            var corpo = await resposta.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.Contains("\"self\"", corpo);
            // Nenhuma dependência externa entra no liveness.
            Assert.DoesNotContain("postgres", corpo);
            Assert.DoesNotContain("pgvector", corpo);
        }

        /// <summary>
        /// O ponto central da separação: banco fora do ar não pode deixar
        /// /health/live vermelho — senão o Docker reinicia o processo à toa.
        /// </summary>
        [Fact]
        public async Task Live_ContinuaOkQuandoBancoEstaIndisponivel()
        {
            await using var app = CriarHost(readyFalha: true);
            await app.StartAsync();
            var client = app.GetTestClient();

            var live = await client.GetAsync(HealthCheckSetup.RotaLive);
            var ready = await client.GetAsync(HealthCheckSetup.RotaReady);

            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        }

        // ── /health/ready ────────────────────────────────────────────────────

        [Fact]
        public async Task Ready_RetornaOkQuandoPostgresEPgvectorRespondem()
        {
            await using var app = CriarHost();
            await app.StartAsync();

            var resposta = await app.GetTestClient().GetAsync(HealthCheckSetup.RotaReady);
            var corpo = await resposta.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.Contains("\"postgres\"", corpo);
            Assert.Contains("\"pgvector\"", corpo);
            // O liveness não é reexecutado aqui.
            Assert.DoesNotContain("\"self\"", corpo);
        }

        [Fact]
        public async Task Ready_Retorna503EIdentificaOCheckQueFalhou()
        {
            await using var app = CriarHost(readyFalha: true);
            await app.StartAsync();

            var resposta = await app.GetTestClient().GetAsync(HealthCheckSetup.RotaReady);
            var corpo = await resposta.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
            Assert.Contains("Unhealthy", corpo);
            Assert.Contains("Falha simulada de PostgreSQL.", corpo);
        }

        [Fact]
        public async Task Respostas_SaoJsonComStatusEDuracao()
        {
            await using var app = CriarHost();
            await app.StartAsync();

            var resposta = await app.GetTestClient().GetAsync(HealthCheckSetup.RotaReady);
            var corpo = await resposta.Content.ReadAsStringAsync();

            Assert.Equal("application/json", resposta.Content.Headers.ContentType?.MediaType);
            Assert.Contains("\"status\":\"Healthy\"", corpo);
            Assert.Contains("\"duracaoMs\"", corpo);
        }

        // ── Acesso anônimo ───────────────────────────────────────────────────

        /// <summary>
        /// Com uma política global que nega tudo, as sondas precisam continuar
        /// passando — é isso que o AllowAnonymous() garante. A rota comum serve de
        /// controle: se ela também passasse, o teste não estaria provando nada.
        /// </summary>
        [Fact]
        public async Task AmbasAsRotas_SaoAnonimasMesmoComPoliticaGlobalQueNega()
        {
            await using var app = CriarHost(comFallbackQueNega: true);
            await app.StartAsync();
            var client = app.GetTestClient();

            var live = await client.GetAsync(HealthCheckSetup.RotaLive);
            var ready = await client.GetAsync(HealthCheckSetup.RotaReady);
            var comum = await client.GetAsync("/rota-comum");

            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, comum.StatusCode); // controle
        }

        // ── Guarda de onboarding ─────────────────────────────────────────────

        /// <summary>
        /// Exercita a MESMA função que a guarda em Program.cs usa — não uma cópia.
        /// Sem a isenção, um cookie de perfil incompleto faria as sondas
        /// responderem 403 e a plataforma leria isso como aplicação doente.
        /// </summary>
        [Theory]
        [InlineData("/health/live")]
        [InlineData("/health/ready")]
        [InlineData("/health/live/")]
        [InlineData("/HEALTH/READY")] // comparação é case-insensitive
        public async Task RotasDeHealth_SaoIsentasDaGuardaDeOnboarding(string caminho)
        {
            Assert.True(RotasIsentasOnboarding.EhIsenta(caminho));
            await Task.CompletedTask;
        }

        [Theory]
        [InlineData("/api/contratos")]
        [InlineData("/api/pagamentos/x/liberar")]
        [InlineData("/healthz")]        // parecido, mas não é a rota isenta
        [InlineData("/api/health/live")] // idem
        [InlineData(null)]
        public void RotasNaoIsentas_ContinuamBloqueadas(string? caminho)
        {
            Assert.False(RotasIsentasOnboarding.EhIsenta(caminho));
        }

        /// <summary>As isenções que já existiam antes não podem ter sido perdidas.</summary>
        [Theory]
        [InlineData("/api/me")]
        [InlineData("/usuarios/onboarding")]
        [InlineData("/usuarios/logout")]
        [InlineData("/usuarios/login")]
        [InlineData("/usuarios/cadastro")]
        [InlineData("/usuarios/senha/resetar")]
        [InlineData("/swagger")]
        [InlineData("/api/cep/01310100")]
        public void IsencoesPreexistentes_ForamPreservadas(string caminho)
        {
            Assert.True(RotasIsentasOnboarding.EhIsenta(caminho));
        }

        // ── Sonda do Docker ──────────────────────────────────────────────────

        [Theory]
        [InlineData(new[] { "--healthcheck" }, true)]
        [InlineData(new[] { "--HEALTHCHECK" }, true)]
        [InlineData(new[] { "outro", "--healthcheck" }, true)]
        [InlineData(new string[0], false)]
        [InlineData(new[] { "--urls", "http://+:9000" }, false)]
        public void SondaHealthCheck_DetectaOArgumentoCorretamente(string[] args, bool esperado)
        {
            Assert.Equal(esperado, SondaHealthCheck.FoiSolicitada(args));
        }
    }
}
