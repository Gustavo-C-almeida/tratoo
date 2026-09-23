using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using System.Net.Http.Json;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Comportamento de X-Forwarded-For / X-Forwarded-Proto: quem é aceito, quantos
    /// saltos são percorridos e o que acontece com header forjado.
    ///
    /// Todos os endereços abaixo são de faixas reservadas para documentação
    /// (RFC 5737) ou não-roteáveis (RFC 6598), nunca de tráfego real de produção.
    /// </summary>
    public class ForwardedHeadersTests
    {
        private const string IpCliente = "203.0.113.45";   // TEST-NET-3 — cliente final
        private const string IpBorda   = "198.51.100.200"; // TEST-NET-2 — pool de borda (salto 2)
        private const string IpBorda2  = "198.51.100.201"; // outro nó do mesmo pool

        private static async Task<EcoResposta> EcoAsync(
            WebApplication app, string peer, string? forwardedFor = null,
            string? forwardedProto = null, string host = "localhost")
        {
            var resposta = await app.GetTestClient()
                .SendAsync(Montar(peer, forwardedFor, forwardedProto, host));
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

        // ── Comportamento básico ─────────────────────────────────────────────

        [Fact]
        public async Task SemForwardedHeaders_UsaIpDaConexao()
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(app, peer: TestHostFactory.PeerConfiavel);

            Assert.Equal(TestHostFactory.PeerConfiavel, eco.Ip);
            Assert.Equal("http", eco.Scheme);
            Assert.False(eco.IsHttps);
        }

        [Fact]
        public async Task PeerConfiavel_ComXForwardedProto_CorrigeSchemeEIsHttps()
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerConfiavel,
                forwardedFor: $"{IpCliente}, {IpBorda}", forwardedProto: "https");

            Assert.Equal("https", eco.Scheme);
            Assert.True(eco.IsHttps);
        }

        [Fact]
        public async Task Desabilitado_IgnoraForwardedHeaders()
        {
            await using var app = TestHostFactory.Criar(TestHostFactory.Config(habilitado: false));
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerConfiavel,
                forwardedFor: $"{IpCliente}, {IpBorda}", forwardedProto: "https");

            Assert.Equal(TestHostFactory.PeerConfiavel, eco.Ip);
            Assert.Equal("http", eco.Scheme);
        }

        // ── Trust boundary: o gate de peer confiável ─────────────────────────

        /// <summary>
        /// Núcleo da proteção anti-spoofing: quem fala direto com o Kestrel de fora das
        /// faixas confiáveis tem X-Forwarded-For E X-Forwarded-Proto descartados.
        /// </summary>
        [Fact]
        public async Task PeerNaoConfiavel_TemHeadersForjadosDescartados()
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerNaoConfiavel,
                forwardedFor: $"{IpCliente}, {IpBorda}", forwardedProto: "https");

            Assert.Equal(TestHostFactory.PeerNaoConfiavel, eco.Ip); // IP real de quem conectou
            Assert.Equal("http", eco.Scheme);                       // proto forjado descartado
            Assert.False(eco.IsHttps);
        }

        /// <summary>
        /// Nem forjar uma cadeia longa ajuda: o gate corta antes de o middleware olhar.
        /// </summary>
        [Fact]
        public async Task PeerNaoConfiavel_NaoEscapaComCadeiaLonga()
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerNaoConfiavel,
                forwardedFor: $"8.8.8.8, 1.1.1.1, {IpCliente}, {IpBorda}");

            Assert.Equal(TestHostFactory.PeerNaoConfiavel, eco.Ip);
        }

        [Theory]
        [InlineData("100.64.0.1")]      // CGNAT — o que o Kestrel vê no Railway
        [InlineData("100.127.255.254")] // outra ponta da faixa 100.64.0.0/10
        [InlineData("::ffff:100.64.0.7")] // IPv4 mapeado em IPv6 (socket dual-stack)
        [InlineData("fd12:0:8:0:2000:9f:8000:1")] // ULA — ingresso observado nos flow logs
        [InlineData("127.0.0.1")]       // loopback (dev local)
        public async Task PeersDentroDasFaixasPadrao_SaoAceitos(string peer)
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(app, peer, forwardedFor: $"{IpCliente}, {IpBorda}");

            Assert.Equal(IpCliente, eco.Ip);
        }

        [Theory]
        [InlineData("192.0.2.66")]    // internet pública
        [InlineData("100.63.255.255")] // logo ABAIXO de 100.64.0.0/10
        [InlineData("100.128.0.0")]    // logo ACIMA de 100.64.0.0/10
        [InlineData("8.8.8.8")]
        public async Task PeersForaDasFaixasPadrao_SaoRejeitados(string peer)
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(app, peer, forwardedFor: $"{IpCliente}, {IpBorda}");

            Assert.Equal(peer, eco.Ip);
        }

        [Fact]
        public async Task PeersConfiaveisCustomizado_SubstituiOPadrao()
        {
            await using var app = TestHostFactory.Criar(
                TestHostFactory.Config(peersConfiaveis: new[] { "192.0.2.0/24" }));
            await app.StartAsync();

            // Agora 192.0.2.66 é confiável...
            var aceito = await EcoAsync(app, "192.0.2.66", forwardedFor: $"{IpCliente}, {IpBorda}");
            Assert.Equal(IpCliente, aceito.Ip);

            // ...e a faixa CGNAT, que era padrão, deixou de ser.
            var rejeitado = await EcoAsync(app, "100.64.0.1", forwardedFor: $"{IpCliente}, {IpBorda}");
            Assert.Equal("100.64.0.1", rejeitado.Ip);
        }

        /// <summary>Lista vazia = opt-out explícito: volta a aceitar de qualquer peer.</summary>
        [Fact]
        public async Task PeersConfiaveisVazio_DesligaOGate()
        {
            await using var app = TestHostFactory.Criar(
                TestHostFactory.Config(peersConfiaveis: Array.Empty<string>()));
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerNaoConfiavel, forwardedFor: $"{IpCliente}, {IpBorda}");

            Assert.Equal(IpCliente, eco.Ip);
        }

        // ── ForwardLimit=2: topologia observada no Railway ───────────────────

        /// <summary>
        /// Topologia real: "&lt;cliente&gt;, &lt;pool de borda&gt;". O pool roda entre vários
        /// IPs, por isso o teste cobre mais de um.
        /// </summary>
        [Theory]
        [InlineData(IpBorda)]
        [InlineData(IpBorda2)]
        public async Task DoisSaltos_ResolveParaOClienteReal(string ipDoPool)
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerConfiavel, forwardedFor: $"{IpCliente}, {ipDoPool}");

            Assert.Equal(IpCliente, eco.Ip);
        }

        /// <summary>
        /// Defesa em profundidade: se algum dia um proxy ANEXAR em vez de regenerar o
        /// header, as entradas que o cliente injetou ficam à esquerda da janela de
        /// ForwardLimit=2 e são descartadas.
        /// </summary>
        [Fact]
        public async Task ComMaisEntradasQueForwardLimit_DescartaOExcedenteDaEsquerda()
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerConfiavel,
                forwardedFor: $"8.8.8.8, 1.1.1.1, {IpCliente}, {IpBorda}");

            Assert.Equal(IpCliente, eco.Ip);
            Assert.NotEqual("8.8.8.8", eco.Ip);
        }

        /// <summary>Regressão: ForwardLimit=1 resolveria para o pool de borda, não o cliente.</summary>
        [Fact]
        public async Task ForwardLimit1_ResolveriaErroneamenteParaOPoolDeBorda()
        {
            await using var app = TestHostFactory.Criar(TestHostFactory.Config(forwardLimit: 1));
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerConfiavel, forwardedFor: $"{IpCliente}, {IpBorda}");

            Assert.Equal(IpBorda, eco.Ip);
        }

        /// <summary>Sem nenhuma configuração, o padrão do projeto resolve a topologia real.</summary>
        [Fact]
        public async Task ConfiguracaoPadrao_ResolveATopologiaDeDoisSaltos()
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerConfiavel, forwardedFor: $"{IpCliente}, {IpBorda}");

            Assert.Equal(IpCliente, eco.Ip);
        }

        // ── Armadilha do KnownNetworks (documenta o porquê do gate) ──────────

        /// <summary>
        /// Por que o trust boundary NÃO usa KnownNetworks: essas listas validam cada
        /// salto. Cobrindo só a faixa do peer, o middleware para no primeiro salto e
        /// adota o IP do pool de borda como cliente — o mesmo bug do ForwardLimit=1.
        /// Este teste existe para que ninguém "melhore" a config caindo nessa.
        /// </summary>
        [Fact]
        public async Task PinarSoAFaixaDoPeer_QuebraAIdentificacaoDoCliente()
        {
            await using var app = TestHostFactory.Criar(
                TestHostFactory.Config(knownNetworks: new[] { "100.64.0.0/10" }));
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerConfiavel, forwardedFor: $"{IpCliente}, {IpBorda}");

            Assert.Equal(IpBorda, eco.Ip);
            Assert.NotEqual(IpCliente, eco.Ip);
        }

        /// <summary>Cobrindo TODOS os saltos, aí sim KnownNetworks funciona.</summary>
        [Fact]
        public async Task PinarTodosOsSaltos_IdentificaOClienteCorretamente()
        {
            await using var app = TestHostFactory.Criar(
                TestHostFactory.Config(
                    knownNetworks: new[] { "100.64.0.0/10", "198.51.100.0/24" }));
            await app.StartAsync();

            var eco = await EcoAsync(
                app, TestHostFactory.PeerConfiavel, forwardedFor: $"{IpCliente}, {IpBorda}");

            Assert.Equal(IpCliente, eco.Ip);
        }

        // ── HSTS e normalização ──────────────────────────────────────────────

        /// <summary>
        /// HstsMiddleware só emite o header quando Request.IsHttps é true — por isso
        /// UseForwardedHeaders precisa vir antes dele. O host não pode ser localhost:
        /// está em HstsOptions.ExcludedHosts por padrão.
        /// </summary>
        [Fact]
        public async Task Hsts_SoEEmitidoQuandoOProtoEncaminhadoEHttps()
        {
            const string HostPublico = "tratoo.example";

            await using var app = TestHostFactory.Criar(comHsts: true);
            await app.StartAsync();
            var client = app.GetTestClient();

            var semProto = await client.SendAsync(
                Montar(TestHostFactory.PeerConfiavel, IpCliente, host: HostPublico));
            var comProto = await client.SendAsync(
                Montar(TestHostFactory.PeerConfiavel, IpCliente, "https", HostPublico));

            Assert.False(semProto.Headers.Contains("Strict-Transport-Security"));
            Assert.True(comProto.Headers.Contains("Strict-Transport-Security"));
        }

        [Fact]
        public async Task IpV4MapeadoEmIpV6_ENormalizado()
        {
            await using var app = TestHostFactory.Criar();
            await app.StartAsync();

            var eco = await EcoAsync(app, peer: "::ffff:100.64.0.9");

            Assert.Equal("100.64.0.9", eco.Ip);
        }
    }
}
