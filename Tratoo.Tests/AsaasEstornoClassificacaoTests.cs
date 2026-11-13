using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tratoo.Domain.Features.Pagamentos;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Como o AsaasGatewayService classifica cada desfecho de POST /v3/payments/{id}/refund.
    /// Mesma regra de evidência das transferências: só 400 com <c>errors[]</c> e 401 provam que
    /// o estorno NÃO foi feito (falha definitiva — o pagamento pode voltar a Retido). Rede,
    /// timeout, 5xx e qualquer outro status são <see cref="AsaasErroTipo.ResultadoIncerto"/>:
    /// o estorno pode ter acontecido, e o pagamento nunca pode voltar a ser liberável.
    /// </summary>
    public class AsaasEstornoClassificacaoTests
    {
        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _resposta;
            public Handler(Func<HttpRequestMessage, HttpResponseMessage> resposta) => _resposta = resposta;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
                Task.FromResult(_resposta(r));
        }

        private static AsaasGatewayService Gateway(Func<HttpRequestMessage, HttpResponseMessage> resposta) => new(
            new HttpClient(new Handler(resposta)),
            Options.Create(new AsaasConfig { BaseUrl = "https://sandbox.invalid/api/v3", ApiKey = "chave-de-teste" }),
            NullLogger<AsaasGatewayService>.Instance);

        private static HttpResponseMessage Resposta(HttpStatusCode status, string corpo) =>
            new(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };

        private const string ComErros = "{\"errors\":[{\"code\":\"invalid_action\",\"description\":\"Cobrança já estornada\"}]}";

        [Fact]
        public async Task Sucesso_nao_lanca()
        {
            string? caminho = null;
            var gateway = Gateway(r => { caminho = r.RequestUri!.AbsolutePath; return Resposta(HttpStatusCode.OK, "{\"id\":\"pay_1\",\"status\":\"REFUNDED\"}"); });
            await gateway.EstornarCobrancaAsync("pay_1");
            Assert.EndsWith("/payments/pay_1/refund", caminho);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, ComErros)]
        [InlineData(HttpStatusCode.Unauthorized, "")]
        public async Task Recusa_documentada_e_falha_definitiva(HttpStatusCode status, string corpo)
        {
            var ex = await Assert.ThrowsAsync<AsaasIntegracaoException>(
                () => Gateway(_ => Resposta(status, corpo)).EstornarCobrancaAsync("pay_1"));
            Assert.NotEqual(AsaasErroTipo.ResultadoIncerto, ex.TipoErro);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "{}")]
        [InlineData(HttpStatusCode.BadGateway, ComErros)]
        [InlineData(HttpStatusCode.GatewayTimeout, "")]
        [InlineData(HttpStatusCode.NotFound, "{}")]
        [InlineData(HttpStatusCode.Conflict, ComErros)]
        [InlineData(HttpStatusCode.UnprocessableEntity, ComErros)]
        [InlineData(HttpStatusCode.TooManyRequests, "")]
        [InlineData(HttpStatusCode.BadRequest, "<html>erro</html>")]
        public async Task Resposta_sem_evidencia_e_resultado_incerto(HttpStatusCode status, string corpo)
        {
            var ex = await Assert.ThrowsAsync<AsaasIntegracaoException>(
                () => Gateway(_ => Resposta(status, corpo)).EstornarCobrancaAsync("pay_1"));
            Assert.Equal(AsaasErroTipo.ResultadoIncerto, ex.TipoErro);
        }

        [Fact]
        public async Task Falha_de_rede_e_resultado_incerto()
        {
            var ex = await Assert.ThrowsAsync<AsaasIntegracaoException>(
                () => Gateway(_ => throw new HttpRequestException("conexão encerrada")).EstornarCobrancaAsync("pay_1"));
            Assert.Equal(AsaasErroTipo.ResultadoIncerto, ex.TipoErro);
        }

        [Fact]
        public async Task Timeout_e_resultado_incerto()
        {
            var ex = await Assert.ThrowsAsync<AsaasIntegracaoException>(
                () => Gateway(_ => throw new TaskCanceledException("timeout")).EstornarCobrancaAsync("pay_1"));
            Assert.Equal(AsaasErroTipo.ResultadoIncerto, ex.TipoErro);
        }
    }
}
