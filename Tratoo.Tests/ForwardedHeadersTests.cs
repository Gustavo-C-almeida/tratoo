using Microsoft.AspNetCore.TestHost;
using System.Net.Http.Json;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Cenários 1 a 6 e 10 do plano de validação: o que a aplicação enxerga como
    /// IP e protocolo do cliente atrás (e fora) de um reverse proxy.
    /// </summary>
    public class ForwardedHeadersTests
    {
        // IPs de documentação (RFC 5737) — nenhum endereço real de infraestrutura.
        private const string IpCliente = "203.0.113.45";
        private const string IpProxy   = "198.51.100.7";
        private const string IpOutro   = "203.0.113.200";

        private static ForwardedHeadersSettings ConfiandoNoProxyImediato() =>
            new() { Habilitado = true, ForwardLimit = 1, ConfiarNoProxyImediato = true };

        private static async Task<EcoResposta> EcoAsync(
            HttpClient client, string peer, string? forwardedFor = null, string? forwardedProto = null)
        {
            var resposta = await client.SendAsync(Montar(peer, forwardedFor, forwardedProto));
            resposta.EnsureSuccessStatusCode();

            return (await resposta.Content.ReadFromJsonAsync<EcoResposta>())!;
        }

        private static HttpRequestMessage Montar(
            string peer, string? forwardedFor = null, string? forwardedProto = null,
            string host = "localhost")
        {
            var requisicao = new HttpRequestMessage(HttpMethod.Get, $"http://{host}/eco");
            requisicao.Headers.Add(TestHostFactory.HeaderPeer, peer);

            if (forwardedFor is not null)
                requisicao.Headers.Add("X-Forwarded-For", forwardedFor);

            if (forwardedProto is not null)
                requisicao.Headers.Add("X-Forwarded-Proto", forwardedProto);

            return requisicao;
        }

        // ── 1. Requisição sem forwarded headers ──────────────────────────────
        [Fact]
        public async Task SemForwardedHeaders_UsaIpDaConexao()
        {
            await using var app = TestHostFactory.Criar(ConfiandoNoProxyImediato());
            await app.StartAsync();

            var eco = await EcoAsync(app.GetTestClient(), peer: IpCliente);

            Assert.Equal(IpCliente, eco.Ip);
            Assert.Equal("http", eco.Scheme);
            Assert.False(eco.IsHttps);
        }

        // ── 2. Requisição com X-Forwarded-For ────────────────────────────────
        // ── 4. RemoteIpAddress depois do middleware ──────────────────────────
        [Fact]
        public async Task ComXForwardedFor_AdotaIpDoClienteEmVezDoProxy()
        {
            await using var app = TestHostFactory.Criar(ConfiandoNoProxyImediato());
            await app.StartAsync();

            var eco = await EcoAsync(app.GetTestClient(), peer: IpProxy, forwardedFor: IpCliente);

            Assert.Equal(IpCliente, eco.Ip);
            Assert.NotEqual(IpProxy, eco.Ip);
        }

        // ── 3. X-Forwarded-Proto: https ──────────────────────────────────────
        // ── 5. Request.IsHttps ── 6. Request.Scheme ──────────────────────────
        [Fact]
        public async Task ComXForwardedProto_CorrigeSchemeEIsHttps()
        {
            await using var app = TestHostFactory.Criar(ConfiandoNoProxyImediato());
            await app.StartAsync();

            var eco = await EcoAsync(
                app.GetTestClient(), peer: IpProxy, forwardedFor: IpCliente, forwardedProto: "https");

            Assert.Equal("https", eco.Scheme);
            Assert.True(eco.IsHttps);
        }

        /// <summary>
        /// Regressão do bug: sem o middleware a aplicação registra o IP do proxy e
        /// trata a requisição como http. É exatamente o estado anterior à correção.
        /// </summary>
        [Fact]
        public async Task ComMiddlewareDesabilitado_VoltaAEnxergarOProxy()
        {
            await using var app = TestHostFactory.Criar(
                new ForwardedHeadersSettings { Habilitado = false });
            await app.StartAsync();

            var eco = await EcoAsync(
                app.GetTestClient(), peer: IpProxy, forwardedFor: IpCliente, forwardedProto: "https");

            Assert.Equal(IpProxy, eco.Ip);
            Assert.Equal("http", eco.Scheme);
            Assert.False(eco.IsHttps);
        }

        // ── 10. Proteção contra spoofing ─────────────────────────────────────

        /// <summary>
        /// Cliente não confiável falando direto com o Kestrel: o X-Forwarded-For que
        /// ele mesmo enviou tem de ser IGNORADO quando há lista de proxies confiáveis.
        /// </summary>
        [Fact]
        public async Task ClienteNaoConfiavel_NaoConsegueForjarXForwardedFor()
        {
            var settings = new ForwardedHeadersSettings
            {
                ConfiarNoProxyImediato = false,
                KnownProxies = new[] { IpProxy }
            };

            await using var app = TestHostFactory.Criar(settings);
            await app.StartAsync();

            var eco = await EcoAsync(
                app.GetTestClient(), peer: IpOutro, forwardedFor: "8.8.8.8", forwardedProto: "https");

            Assert.Equal(IpOutro, eco.Ip);    // continua sendo o IP real da conexão
            Assert.Equal("http", eco.Scheme); // e o proto forjado também é descartado
        }

        [Fact]
        public async Task ProxyConhecido_TemSeuXForwardedForAceito()
        {
            var settings = new ForwardedHeadersSettings
            {
                ConfiarNoProxyImediato = false,
                KnownProxies = new[] { IpProxy }
            };

            await using var app = TestHostFactory.Criar(settings);
            await app.StartAsync();

            var eco = await EcoAsync(app.GetTestClient(), peer: IpProxy, forwardedFor: IpCliente);

            Assert.Equal(IpCliente, eco.Ip);
        }

        [Fact]
        public async Task RedeConhecidaEmCidr_ReconheceProxyDaFaixa()
        {
            var settings = new ForwardedHeadersSettings
            {
                ConfiarNoProxyImediato = false,
                KnownNetworks = new[] { "198.51.100.0/24" }
            };

            await using var app = TestHostFactory.Criar(settings);
            await app.StartAsync();

            var eco = await EcoAsync(app.GetTestClient(), peer: IpProxy, forwardedFor: IpCliente);

            Assert.Equal(IpCliente, eco.Ip);
        }

        /// <summary>
        /// ConfiarNoProxyImediato=false sem nenhum proxy configurado NÃO pode virar
        /// "confia em todo mundo" — listas vazias desligam a checagem no framework.
        /// O setup restaura o default de loopback nesse caso.
        /// </summary>
        [Fact]
        public async Task SemProxyConfigurado_ENaoConfiandoNoPeer_IgnoraHeader()
        {
            var settings = new ForwardedHeadersSettings { ConfiarNoProxyImediato = false };

            await using var app = TestHostFactory.Criar(settings);
            await app.StartAsync();

            var eco = await EcoAsync(app.GetTestClient(), peer: IpProxy, forwardedFor: IpCliente);

            Assert.Equal(IpProxy, eco.Ip);
        }

        /// <summary>
        /// A defesa central contra spoofing quando ConfiarNoProxyImediato=true:
        /// ForwardLimit=1 faz o middleware ler a ÚLTIMA entrada da lista — a que o
        /// proxy de borda anexou. O valor que o cliente tentou injetar fica à
        /// esquerda e é descartado.
        /// </summary>
        [Fact]
        public async Task ComForwardLimit1_UsaUltimaEntradaDaCadeia()
        {
            await using var app = TestHostFactory.Criar(ConfiandoNoProxyImediato());
            await app.StartAsync();

            var eco = await EcoAsync(
                app.GetTestClient(),
                peer: IpProxy,
                forwardedFor: "8.8.8.8, 1.1.1.1, " + IpCliente);

            Assert.Equal(IpCliente, eco.Ip);
        }

        /// <summary>
        /// HSTS: o HstsMiddleware só emite o header quando Request.IsHttps é true.
        /// Antes da correção, atrás do proxy o scheme era sempre http — logo o
        /// app.UseHsts() de produção nunca emitia nada.
        ///
        /// O host precisa ser diferente de localhost/127.0.0.1/[::1]: esses estão
        /// em HstsOptions.ExcludedHosts por padrão e nunca recebem o header.
        /// </summary>
        [Fact]
        public async Task Hsts_SoEEmitidoQuandoOProtoEncaminhadoEHttps()
        {
            const string HostPublico = "tratoo.example";

            await using var app = TestHostFactory.Criar(ConfiandoNoProxyImediato(), comHsts: true);
            await app.StartAsync();
            var client = app.GetTestClient();

            var semProto = await client.SendAsync(Montar(IpProxy, IpCliente, host: HostPublico));
            var comProto = await client.SendAsync(Montar(IpProxy, IpCliente, "https", HostPublico));

            Assert.False(semProto.Headers.Contains("Strict-Transport-Security"));
            Assert.True(comProto.Headers.Contains("Strict-Transport-Security"));
        }

        /// <summary>IPv4 mapeado em IPv6 é normalizado antes de ser gravado/particionado.</summary>
        [Fact]
        public async Task IpV4MapeadoEmIpV6_ENormalizado()
        {
            await using var app = TestHostFactory.Criar(ConfiandoNoProxyImediato());
            await app.StartAsync();

            var eco = await EcoAsync(app.GetTestClient(), peer: "::ffff:" + IpCliente);

            Assert.Equal(IpCliente, eco.Ip);
        }
    }
}
