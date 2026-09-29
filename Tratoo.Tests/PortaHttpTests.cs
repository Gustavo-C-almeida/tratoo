using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// PORT → porta do Kestrel. Aqui NÃO há TestServer: sobe o Kestrel de verdade,
    /// num socket de verdade, porque o que pode regredir é justamente o bind
    /// (ex.: alguém volta a pôr ASPNETCORE_URLS no Dockerfile, ou o mapeamento para
    /// http_ports deixa de ser aplicado). As portas são pedidas ao SO para o teste
    /// não depender de 8080/9090 estarem livres na máquina.
    /// </summary>
    public class PortaHttpTests
    {
        private static int PortaLivre()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>Sobe o Kestrel com o MESMO passo do Program.cs.</summary>
        private static WebApplication CriarHostReal(IDictionary<string, string?> config)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(config);

            builder.WebHost.UsarPortaDaPlataforma(builder.Configuration);

            var app = builder.Build();
            app.MapGet(HealthCheckSetup.RotaLive, () => Results.Ok("vivo"));
            return app;
        }

        private static int[] PortasEscutadas(WebApplication app) =>
            app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Select(a => new Uri(a.Replace("+", "localhost").Replace("*", "localhost")).Port)
                .Distinct()
                .ToArray();

        private static async Task<HttpStatusCode> Get(int porta, string rota = HealthCheckSetup.RotaLive)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var resposta = await client.GetAsync($"http://localhost:{porta}{rota}");
            return resposta.StatusCode;
        }

        [Fact]
        public async Task Escuta_na_porta_informada_em_PORT()
        {
            var porta = PortaLivre();
            await using var app = CriarHostReal(new Dictionary<string, string?>
            {
                ["PORT"] = porta.ToString()
            });

            await app.StartAsync();

            Assert.Equal(new[] { porta }, PortasEscutadas(app));
            Assert.Equal(HttpStatusCode.OK, await Get(porta));

            await app.StopAsync();
        }

        [Fact]
        public async Task PORT_vence_o_http_ports_padrao_do_Dockerfile()
        {
            // Cenário de produção: a imagem define ASPNETCORE_HTTP_PORTS=8080 e a
            // Railway injeta PORT. Quem manda é o PORT.
            var portaPlataforma = PortaLivre();
            var portaImagem = PortaLivre();
            await using var app = CriarHostReal(new Dictionary<string, string?>
            {
                ["http_ports"] = portaImagem.ToString(),
                ["PORT"] = portaPlataforma.ToString()
            });

            await app.StartAsync();

            Assert.Equal(new[] { portaPlataforma }, PortasEscutadas(app));
            Assert.Equal(HttpStatusCode.OK, await Get(portaPlataforma));

            await app.StopAsync();
        }

        [Fact]
        public async Task Sem_PORT_usa_o_http_ports_como_fallback()
        {
            var portaImagem = PortaLivre();
            await using var app = CriarHostReal(new Dictionary<string, string?>
            {
                ["http_ports"] = portaImagem.ToString()
            });

            await app.StartAsync();

            Assert.Equal(new[] { portaImagem }, PortasEscutadas(app));
            Assert.Equal(HttpStatusCode.OK, await Get(portaImagem));

            await app.StopAsync();
        }

        [Fact]
        public async Task URLS_explicito_continua_tendo_precedencia_sobre_PORT()
        {
            // launchSettings/--urls em desenvolvimento não podem ser atropelados por
            // uma PORT que por acaso exista na máquina.
            var portaUrls = PortaLivre();
            var portaPlataforma = PortaLivre();
            await using var app = CriarHostReal(new Dictionary<string, string?>
            {
                ["urls"] = $"http://localhost:{portaUrls}",
                ["PORT"] = portaPlataforma.ToString()
            });

            await app.StartAsync();

            Assert.Equal(new[] { portaUrls }, PortasEscutadas(app));

            await app.StopAsync();
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("65536")]
        [InlineData("-1")]
        [InlineData("80 80")]
        public void PORT_invalida_derruba_a_subida(string valor)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["PORT"] = valor })
                .Build();

            Assert.Throws<InvalidOperationException>(() => PortaHttpSetup.LerPortaDaPlataforma(config));
        }

        [Theory]
        // urls explícito vence; curinga vira localhost
        [InlineData("http://+:7000", "9090", "8080", "http://localhost:7000")]
        [InlineData("http://*:7000;http://+:7001", null, null, "http://localhost:7000")]
        // PORT vence http_ports
        [InlineData(null, "9090", "8080", "http://localhost:9090")]
        [InlineData(null, "8080", null, "http://localhost:8080")]
        // sem PORT: http_ports (Dockerfile)
        [InlineData(null, null, "8080", "http://localhost:8080")]
        // nada configurado: fallback
        [InlineData(null, null, null, "http://localhost:8080")]
        public void Sonda_resolve_a_porta_na_mesma_precedencia_do_Kestrel(
            string? urls, string? port, string? httpPorts, string esperado)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["urls"] = urls,
                    ["PORT"] = port,
                    ["http_ports"] = httpPorts
                })
                .Build();

            Assert.Equal(esperado, PortaHttpSetup.ResolverUrlLocal(config));
        }

        [Fact]
        public async Task Sonda_do_HEALTHCHECK_encontra_o_processo_na_porta_dinamica()
        {
            var porta = PortaLivre();
            var config = new Dictionary<string, string?> { ["PORT"] = porta.ToString() };
            await using var app = CriarHostReal(config);
            await app.StartAsync();

            // Mesmo ambiente que o processo servidor enxerga (PORT + http_ports=8080 da imagem).
            var ambienteDaSonda = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>(config) { ["http_ports"] = "8080" })
                .Build();

            Assert.Equal(0, await SondaHealthCheck.ExecutarAsync(ambienteDaSonda));

            await app.StopAsync();
        }

        [Fact]
        public async Task Sonda_do_HEALTHCHECK_falha_quando_ninguem_escuta_na_porta()
        {
            var portaVazia = PortaLivre();
            var ambienteDaSonda = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["PORT"] = portaVazia.ToString() })
                .Build();

            Assert.Equal(1, await SondaHealthCheck.ExecutarAsync(ambienteDaSonda));
        }
    }
}
