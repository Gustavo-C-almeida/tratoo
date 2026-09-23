using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// O valor efetivamente entregue às camadas que persistem IP.
    ///
    /// Os endpoints reais (assinatura de contrato, OTP, dados bancários, login,
    /// cadastro, reset de senha, liberação de pagamento, disputa) capturam o IP
    /// exatamente como as rotas abaixo — <c>ClientRequestInfo.ObterIp(http)</c> /
    /// <c>ObterIpOuNulo(http)</c> — e repassam a string sem transformá-la para
    /// <c>HistoricoAssinatura.Ip</c>, <c>ConsentLog.Ip</c> e
    /// <c>IAuditLogRepository.RegistrarAsync</c>.
    ///
    /// O que se valida aqui é a captura: o que chega ao domínio é o IP do cliente,
    /// não o do proxy. A gravação em si (atribuição direta) não é coberta por teste
    /// porque exigiria banco — ver seção "Pontos que dependem de infraestrutura".
    /// </summary>
    public class CapturaIpAuditoriaTests
    {
        private const string IpCliente = "203.0.113.45";   // TEST-NET-3
        private const string IpBorda   = "198.51.100.200"; // pool de borda (salto 2)

        /// <summary>Espelha a captura feita por ContratoExtensions/UserExtensions/etc.</summary>
        private static void MapearRotasQueRegistramIp(WebApplication app)
        {
            // ContratoExtensions: assinar, solicitar-otp — fallback "desconhecido"
            app.MapPost("/assinatura", (HttpContext http) =>
                Results.Ok(new { ip = ClientRequestInfo.ObterIp(http) }));

            // PagamentoExtensions / ContratoExtensions (entrega): fallback null
            app.MapPost("/pagamento/liberar", (HttpContext http) =>
                Results.Ok(new { ip = ClientRequestInfo.ObterIpOuNulo(http) }));

            // AdminDisputaExtensions: fallback "admin"
            app.MapPost("/admin/disputa", (HttpContext http) =>
                Results.Ok(new { ip = ClientRequestInfo.ObterIp(http, "admin") }));
        }

        private static async Task<string?> PostarAsync(
            WebApplication app, string rota, string peer, string? forwardedFor)
        {
            var requisicao = new HttpRequestMessage(HttpMethod.Post, rota);
            requisicao.Headers.Add(TestHostFactory.HeaderPeer, peer);

            if (forwardedFor is not null)
            {
                requisicao.Headers.Add("X-Forwarded-For", forwardedFor);
                requisicao.Headers.Add("X-Forwarded-Proto", "https");
            }

            var resposta = await app.GetTestClient().SendAsync(requisicao);
            resposta.EnsureSuccessStatusCode();

            using var documento = System.Text.Json.JsonDocument.Parse(
                await resposta.Content.ReadAsStringAsync());

            var propriedade = documento.RootElement.GetProperty("ip");
            return propriedade.ValueKind == System.Text.Json.JsonValueKind.Null
                ? null
                : propriedade.GetString();
        }

        [Theory]
        [InlineData("/assinatura")]
        [InlineData("/pagamento/liberar")]
        [InlineData("/admin/disputa")]
        public async Task AtrasDoProxy_RegistraIpDoClienteENaoDaBorda(string rota)
        {
            await using var app = TestHostFactory.Criar(mapearRotas: MapearRotasQueRegistramIp);
            await app.StartAsync();

            var registrado = await PostarAsync(
                app, rota, TestHostFactory.PeerConfiavel, $"{IpCliente}, {IpBorda}");

            Assert.Equal(IpCliente, registrado);
        }

        [Theory]
        [InlineData("/assinatura")]
        [InlineData("/pagamento/liberar")]
        [InlineData("/admin/disputa")]
        public async Task SemProxy_RegistraIpDaConexaoDireta(string rota)
        {
            await using var app = TestHostFactory.Criar(mapearRotas: MapearRotasQueRegistramIp);
            await app.StartAsync();

            var registrado = await PostarAsync(
                app, rota, TestHostFactory.PeerConfiavel, forwardedFor: null);

            Assert.Equal(TestHostFactory.PeerConfiavel, registrado);
        }

        /// <summary>
        /// Antes da correção, TODO registro de auditoria da plataforma gravava o mesmo
        /// IP interno do proxy — tornando a "prova documental" da assinatura e os logs
        /// do Marco Civil inúteis para identificar o autor.
        /// </summary>
        [Fact]
        public async Task SemForwardedHeaders_TodosOsRegistrosRecebemOIpDoPeer()
        {
            await using var app = TestHostFactory.Criar(
                TestHostFactory.Config(habilitado: false),
                mapearRotas: MapearRotasQueRegistramIp);
            await app.StartAsync();

            var peer = TestHostFactory.PeerConfiavel;
            var cadeia = $"{IpCliente}, {IpBorda}";

            Assert.Equal(peer, await PostarAsync(app, "/assinatura", peer, cadeia));
            Assert.Equal(peer, await PostarAsync(app, "/pagamento/liberar", peer, cadeia));
            Assert.Equal(peer, await PostarAsync(app, "/admin/disputa", peer, cadeia));
        }

        /// <summary>
        /// Auditoria não pode ser envenenada por quem fala direto com o Kestrel: o IP
        /// gravado é o real da conexão, não o que o atacante alegou no header.
        /// </summary>
        [Theory]
        [InlineData("/assinatura")]
        [InlineData("/pagamento/liberar")]
        [InlineData("/admin/disputa")]
        public async Task PeerNaoConfiavel_NaoEnvenenaOIpDeAuditoria(string rota)
        {
            await using var app = TestHostFactory.Criar(mapearRotas: MapearRotasQueRegistramIp);
            await app.StartAsync();

            var registrado = await PostarAsync(
                app, rota, TestHostFactory.PeerNaoConfiavel, $"{IpCliente}, {IpBorda}");

            Assert.Equal(TestHostFactory.PeerNaoConfiavel, registrado);
        }
    }
}
