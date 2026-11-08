using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tratoo.Domain.Features.Pagamentos;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// O corpo das respostas do Asaas nunca chega a log nem à mensagem da exceção (que vai
    /// para o ledger): ele pode ecoar chave PIX, CPF/CNPJ e dados do favorecido. E o log não
    /// chama de "rejeição" uma resposta que não comprova que nada foi criado.
    /// </summary>
    public class AsaasLogSemDadosSensiveisTests
    {
        // Valores que só existem no CORPO da resposta: se aparecerem em log ou exceção, vazou.
        private const string ChaveEcoada = "fulano.secreto@exemplo.com.br";
        private const string CpfEcoado = "98765432100";
        private static readonly string ErroComEco =
            "{\"errors\":[{\"code\":\"invalid_pixAddressKey\",\"description\":\"A chave " + ChaveEcoada +
            " do CPF " + CpfEcoado + " não foi encontrada\"}]}";
        private static readonly string CorpoBrutoComEco =
            "<html>upstream error for key " + ChaveEcoada + " cpf " + CpfEcoado + "</html>";
        private static readonly string SucessoSemIdComEco =
            "{\"status\":\"PENDING\",\"pixAddressKey\":\"" + ChaveEcoada + "\",\"cpfCnpj\":\"" + CpfEcoado + "\"}";

        private static readonly AsaasTransferenciaPixRequest Pedido = new(150m, "12345678909", "CPF", "Liberação contrato");

        private sealed class HandlerFixo : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> _resposta;
            public HandlerFixo(Func<HttpResponseMessage> resposta) => _resposta = resposta;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
                Task.FromResult(_resposta());
        }

        private static (AsaasGatewayService Gateway, CapturaDeLog Log) Montar(HttpStatusCode status, string corpo)
        {
            var log = new CapturaDeLog();
            var gateway = new AsaasGatewayService(
                new HttpClient(new HandlerFixo(() => new HttpResponseMessage(status)
                {
                    Content = new StringContent(corpo, Encoding.UTF8, "application/json")
                })),
                Options.Create(new AsaasConfig { BaseUrl = "https://sandbox.invalid/api/v3", ApiKey = "chave-de-teste" }),
                log.Para<AsaasGatewayService>());
            return (gateway, log);
        }

        private static void SemVazamento(string texto)
        {
            Assert.DoesNotContain(ChaveEcoada, texto);
            Assert.DoesNotContain(CpfEcoado, texto);
        }

        public static IEnumerable<object[]> RespostasIncertas() => new[]
        {
            new object[] { HttpStatusCode.InternalServerError, CorpoBrutoComEco },
            new object[] { HttpStatusCode.BadGateway, ErroComEco },
            new object[] { HttpStatusCode.UnprocessableEntity, ErroComEco },
            new object[] { HttpStatusCode.TooManyRequests, ErroComEco },
            new object[] { HttpStatusCode.BadRequest, CorpoBrutoComEco },
            new object[] { HttpStatusCode.OK, SucessoSemIdComEco },
        };

        [Theory]
        [MemberData(nameof(RespostasIncertas))]
        public async Task Resposta_incerta_nao_vaza_o_corpo_e_nao_e_chamada_de_rejeicao(HttpStatusCode status, string corpo)
        {
            var (gateway, log) = Montar(status, corpo);

            var ex = await Assert.ThrowsAsync<AsaasIntegracaoException>(() => gateway.CriarTransferenciaPixAsync(Pedido));

            Assert.Equal(AsaasErroTipo.ResultadoIncerto, ex.TipoErro);
            SemVazamento(ex.Message);
            SemVazamento(ex.ResumoResposta);
            SemVazamento(log.Tudo);
            Assert.Contains("resultado incerto", log.Tudo);
            Assert.DoesNotContain("recusou", log.Tudo);
            Assert.DoesNotContain("rejeitou", log.Tudo);
        }

        [Fact]
        public async Task Rejeicao_comprovada_nao_vaza_o_corpo_e_registra_so_os_codigos()
        {
            var (gateway, log) = Montar(HttpStatusCode.BadRequest, ErroComEco);

            var ex = await Assert.ThrowsAsync<AsaasIntegracaoException>(() => gateway.CriarTransferenciaPixAsync(Pedido));

            Assert.NotEqual(AsaasErroTipo.ResultadoIncerto, ex.TipoErro);
            SemVazamento(ex.Message);
            SemVazamento(log.Tudo);
            Assert.Contains("recusou", log.Tudo);
            Assert.Contains("invalid_pixAddressKey", log.Tudo); // o código da API ajuda o diagnóstico
            Assert.DoesNotContain("resultado incerto", log.Tudo);
        }

        [Fact]
        public async Task Chave_do_pedido_aparece_so_mascarada()
        {
            var (gateway, log) = Montar(HttpStatusCode.InternalServerError, "{}");
            await Assert.ThrowsAsync<AsaasIntegracaoException>(() => gateway.CriarTransferenciaPixAsync(Pedido));
            Assert.DoesNotContain(Pedido.ChavePix, log.Tudo);
            Assert.Contains("*******8909", log.Tudo);
        }

        [Fact]
        public async Task Erro_no_estorno_nao_vaza_o_corpo()
        {
            var (gateway, log) = Montar(HttpStatusCode.BadRequest, ErroComEco);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.EstornarCobrancaAsync("pay_123"));
            SemVazamento(ex.Message);
            SemVazamento(log.Tudo);
        }

        [Theory]
        [InlineData(null, "corpo vazio")]
        [InlineData("", "corpo vazio")]
        [InlineData("<html>x</html>", "corpo não-JSON (14 bytes)")]
        [InlineData("{\"id\":\"x\"}", "JSON sem errors[] (10 bytes)")]
        [InlineData("{\"errors\":[]}", "errors[] vazio (13 bytes)")]
        [InlineData("{\"errors\":[{\"code\":\"invalid_value\",\"description\":\"segredo\"}]}", "códigos de erro: invalid_value (")]
        [InlineData("{\"errors\":[{\"code\":\"a b@c.com\"},{\"description\":\"sem code\"}]}", "códigos de erro: ?, ? (")]
        public void Resumo_traz_so_codigos_seguros_e_tamanho(string? corpo, string esperadoNoInicio)
        {
            var resumo = AsaasIntegracaoException.ResumirResposta(corpo);
            Assert.StartsWith(esperadoNoInicio, resumo);
            Assert.DoesNotContain("segredo", resumo);
            Assert.DoesNotContain("@", resumo);
        }
    }
}
