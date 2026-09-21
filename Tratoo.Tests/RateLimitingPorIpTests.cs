using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Threading.RateLimiting;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Cenário 9: o rate limiting tem de contar por IP do cliente — o IP já
    /// corrigido pelos forwarded headers — e não por proxy nem globalmente.
    /// </summary>
    public class RateLimitingPorIpTests
    {
        private const string IpProxy    = "198.51.100.7";
        private const string IpClienteA = "203.0.113.10";
        private const string IpClienteB = "203.0.113.20";

        private static ForwardedHeadersSettings AtrasDeProxy() =>
            new() { Habilitado = true, ForwardLimit = 1, ConfiarNoProxyImediato = true };

        private static void MapearRotasLimitadas(WebApplication app)
        {
            app.MapGet("/login", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaLogin);

            app.MapGet("/senha", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaSenha);
        }

        private static Task<HttpResponseMessage> ChamarAsync(
            HttpClient client, string rota, string ipCliente)
        {
            var requisicao = new HttpRequestMessage(HttpMethod.Get, rota);
            requisicao.Headers.Add(TestHostFactory.HeaderPeer, IpProxy);
            requisicao.Headers.Add("X-Forwarded-For", ipCliente);
            return client.SendAsync(requisicao);
        }

        /// <summary>
        /// Política "login": 10/minuto. A 11ª do MESMO cliente é barrada; o
        /// cliente seguinte, com outro IP, continua passando.
        /// </summary>
        [Fact]
        public async Task Login_ContaPorIpDoCliente_NaoPorProxy()
        {
            await using var app = TestHostFactory.Criar(
                AtrasDeProxy(), comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();
            var client = app.GetTestClient();

            for (var i = 1; i <= 10; i++)
            {
                var permitida = await ChamarAsync(client, "/login", IpClienteA);
                Assert.True(permitida.IsSuccessStatusCode, $"requisição {i} deveria passar");
            }

            var excedente = await ChamarAsync(client, "/login", IpClienteA);
            Assert.Equal(HttpStatusCode.TooManyRequests, excedente.StatusCode);

            // Outro usuário, atrás do MESMO proxy, não pode ser penalizado.
            var outroCliente = await ChamarAsync(client, "/login", IpClienteB);
            Assert.True(outroCliente.IsSuccessStatusCode);
        }

        /// <summary>
        /// O middleware mantém um único PartitionedRateLimiter para todas as
        /// políticas: se a chave não incluísse o nome da política, esgotar "senha"
        /// (3/min) derrubaria "login" do mesmo IP.
        /// </summary>
        [Fact]
        public async Task PoliticasDiferentes_NaoCompartilhamBalde()
        {
            await using var app = TestHostFactory.Criar(
                AtrasDeProxy(), comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();
            var client = app.GetTestClient();

            for (var i = 1; i <= 3; i++)
                Assert.True((await ChamarAsync(client, "/senha", IpClienteA)).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(client, "/senha", IpClienteA)).StatusCode);

            // "login" do mesmo IP segue intacta.
            Assert.True((await ChamarAsync(client, "/login", IpClienteA)).IsSuccessStatusCode);
        }

        /// <summary>
        /// Sem os forwarded headers, todos os clientes chegam com o IP do proxy e
        /// caem na mesma partição — o bug de disponibilidade que a correção elimina.
        /// </summary>
        [Fact]
        public async Task SemForwardedHeaders_ClientesDistintosCompartilhamOLimite()
        {
            await using var app = TestHostFactory.Criar(
                new ForwardedHeadersSettings { Habilitado = false },
                comRateLimiter: true,
                mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();
            var client = app.GetTestClient();

            for (var i = 1; i <= 3; i++)
                Assert.True((await ChamarAsync(client, "/senha", IpClienteA)).IsSuccessStatusCode);

            var vitimaInocente = await ChamarAsync(client, "/senha", IpClienteB);
            Assert.Equal(HttpStatusCode.TooManyRequests, vitimaInocente.StatusCode);
        }

        /// <summary>
        /// Caracterização do bug original: RateLimiterOptions.AddFixedWindowLimiter
        /// não particiona por nada — usa uma chave constante. Este teste roda contra
        /// a API do framework, e não contra o código do Tratoo, para documentar
        /// POR QUE a configuração antiga precisava sair.
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
                // Exatamente como o Program.cs estava antes da correção.
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

            // Três IPs DIFERENTES gastam o mesmo balde de 3.
            Assert.True((await Chamar("203.0.113.1")).IsSuccessStatusCode);
            Assert.True((await Chamar("203.0.113.2")).IsSuccessStatusCode);
            Assert.True((await Chamar("203.0.113.3")).IsSuccessStatusCode);

            var quarto = await Chamar("203.0.113.4");
            Assert.Equal(HttpStatusCode.TooManyRequests, quarto.StatusCode);
        }
    }
}
