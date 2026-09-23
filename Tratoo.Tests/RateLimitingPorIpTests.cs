using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.RateLimiting;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// O rate limiting tem de contar por IP do cliente — o IP já corrigido pelos
    /// forwarded headers — e não por proxy nem globalmente.
    /// </summary>
    public class RateLimitingPorIpTests
    {
        private const string IpBorda    = "198.51.100.200"; // pool de borda (salto 2)
        private const string IpClienteA = "203.0.113.10";
        private const string IpClienteB = "203.0.113.20";

        private static void MapearRotasLimitadas(WebApplication app)
        {
            app.MapGet("/login", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaLogin);

            app.MapGet("/senha", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaSenha);
        }

        /// <summary>Requisição com a cadeia de 2 saltos real, vinda de um peer confiável.</summary>
        private static Task<HttpResponseMessage> ChamarAsync(
            WebApplication app, string rota, string ipCliente)
        {
            var requisicao = new HttpRequestMessage(HttpMethod.Get, rota);
            requisicao.Headers.Add(TestHostFactory.HeaderPeer, TestHostFactory.PeerConfiavel);
            requisicao.Headers.Add("X-Forwarded-For", $"{ipCliente}, {IpBorda}");
            return app.GetTestClient().SendAsync(requisicao);
        }

        /// <summary>
        /// Política "login": 10/minuto. A 11ª do MESMO cliente é barrada; outro cliente,
        /// atrás do mesmo nó de borda, continua passando.
        /// </summary>
        [Fact]
        public async Task Login_ContaPorIpDoCliente_NaoPorBorda()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 10; i++)
                Assert.True((await ChamarAsync(app, "/login", IpClienteA)).IsSuccessStatusCode,
                    $"requisição {i} deveria passar");

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(app, "/login", IpClienteA)).StatusCode);

            Assert.True((await ChamarAsync(app, "/login", IpClienteB)).IsSuccessStatusCode);
        }

        /// <summary>
        /// O middleware mantém um único PartitionedRateLimiter para todas as políticas:
        /// se a chave não incluísse o nome da política, esgotar "senha" (3/min)
        /// derrubaria "login" do mesmo IP.
        /// </summary>
        [Fact]
        public async Task PoliticasDiferentes_NaoCompartilhamBalde()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 3; i++)
                Assert.True((await ChamarAsync(app, "/senha", IpClienteA)).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(app, "/senha", IpClienteA)).StatusCode);

            Assert.True((await ChamarAsync(app, "/login", IpClienteA)).IsSuccessStatusCode);
        }

        /// <summary>
        /// Dois clientes reais distintos que caem no MESMO nó de borda precisam contar
        /// separado. Com ForwardLimit=1 ambos resolveriam para o IP da borda e
        /// dividiriam a cota.
        /// </summary>
        [Fact]
        public async Task DoisClientesNoMesmoNoDeBorda_NaoCompartilhamCota()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 3; i++)
                Assert.True((await ChamarAsync(app, "/senha", IpClienteA)).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(app, "/senha", IpClienteA)).StatusCode);

            Assert.True((await ChamarAsync(app, "/senha", IpClienteB)).IsSuccessStatusCode);
        }

        /// <summary>
        /// Sem os forwarded headers, todos os clientes chegam com o IP do peer e caem na
        /// mesma partição — o bug de disponibilidade que a correção elimina.
        /// </summary>
        [Fact]
        public async Task SemForwardedHeaders_ClientesDistintosCompartilhamOLimite()
        {
            await using var app = TestHostFactory.Criar(
                TestHostFactory.Config(habilitado: false),
                comRateLimiter: true,
                mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 3; i++)
                Assert.True((await ChamarAsync(app, "/senha", IpClienteA)).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(app, "/senha", IpClienteB)).StatusCode);
        }

        /// <summary>
        /// Um atacante falando direto com o Kestrel não consegue escapar do rate limit
        /// forjando X-Forwarded-For: o gate descarta o header e ele é contado pelo IP
        /// real da conexão.
        /// </summary>
        [Fact]
        public async Task PeerNaoConfiavel_NaoEscapaDoLimiteForjandoIp()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            // Cada requisição alega um IP de cliente diferente — se o header fosse
            // aceito, cada uma cairia numa partição nova e o limite nunca seria atingido.
            async Task<HttpResponseMessage> ChamarForjando(string ipFalso)
            {
                var requisicao = new HttpRequestMessage(HttpMethod.Get, "/senha");
                requisicao.Headers.Add(TestHostFactory.HeaderPeer, TestHostFactory.PeerNaoConfiavel);
                requisicao.Headers.Add("X-Forwarded-For", $"{ipFalso}, {IpBorda}");
                return await app.GetTestClient().SendAsync(requisicao);
            }

            Assert.True((await ChamarForjando("203.0.113.1")).IsSuccessStatusCode);
            Assert.True((await ChamarForjando("203.0.113.2")).IsSuccessStatusCode);
            Assert.True((await ChamarForjando("203.0.113.3")).IsSuccessStatusCode);

            // 4ª tentativa: o limite de 3/min já foi consumido pelo IP real do atacante.
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarForjando("203.0.113.4")).StatusCode);
        }

        /// <summary>
        /// Caracterização do bug original: RateLimiterOptions.AddFixedWindowLimiter não
        /// particiona por nada — usa uma chave constante. Roda contra a API do framework,
        /// não contra o código do Tratoo, para documentar por que a config antiga saiu.
        /// </summary>
        [Fact]
        public async Task AddFixedWindowLimiter_DoFramework_UsaUmUnicoBaldeGlobal()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            builder.Services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter("legado", cfg =>
                {
                    cfg.Window = TimeSpan.FromMinutes(1);
                    cfg.PermitLimit = 3;
                    cfg.QueueLimit = 0;
                    cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                });
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });

            await using var app = builder.Build();

            app.Use(async (context, next) =>
            {
                var peer = context.Request.Headers[TestHostFactory.HeaderPeer].ToString();
                if (IPAddress.TryParse(peer, out var endereco))
                    context.Connection.RemoteIpAddress = endereco;
                await next();
            });

            app.UseRateLimiter();
            app.MapGet("/legado", () => Results.Ok()).RequireRateLimiting("legado");

            await app.StartAsync();
            var client = app.GetTestClient();

            async Task<HttpResponseMessage> Chamar(string ip)
            {
                var requisicao = new HttpRequestMessage(HttpMethod.Get, "/legado");
                requisicao.Headers.Add(TestHostFactory.HeaderPeer, ip);
                return await client.SendAsync(requisicao);
            }

            Assert.True((await Chamar("203.0.113.1")).IsSuccessStatusCode);
            Assert.True((await Chamar("203.0.113.2")).IsSuccessStatusCode);
            Assert.True((await Chamar("203.0.113.3")).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await Chamar("203.0.113.4")).StatusCode);
        }
    }
}
