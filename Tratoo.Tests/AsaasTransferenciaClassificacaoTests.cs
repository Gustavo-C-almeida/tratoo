using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tratoo.Domain.Features.Pagamentos;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Como o AsaasGatewayService classifica cada desfecho de POST /v3/transfers. A regra:
    /// só as respostas que a documentação oficial descreve para o endpoint (400 com
    /// <c>errors[]</c> e 401) comprovam que a transferência NÃO foi criada; o resto é
    /// <see cref="AsaasErroTipo.ResultadoIncerto"/> e nunca volta o pagamento para Retido.
    /// </summary>
    public class AsaasTransferenciaClassificacaoTests
    {
        private sealed class HandlerFalso : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> _resposta;
            public int Chamadas { get; private set; }
            public string? UltimoCorpo { get; private set; }
            public HandlerFalso(Func<HttpResponseMessage> resposta) => _resposta = resposta;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Chamadas++;
                UltimoCorpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
                return _resposta();
            }
        }

        private static AsaasGatewayService Gateway(HttpMessageHandler handler) => new(
            new HttpClient(handler),
            Options.Create(new AsaasConfig { BaseUrl = "https://sandbox.invalid/api/v3", ApiKey = "chave-de-teste" }),
            NullLogger<AsaasGatewayService>.Instance);

        private static readonly AsaasTransferenciaPixRequest Pedido = new(150m, "12345678909", "CPF", "Liberação contrato");

        private static HttpResponseMessage Resposta(HttpStatusCode status, string corpo) =>
            new(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };

        private static async Task<AsaasErroTipo> ClassificacaoAsync(Func<HttpResponseMessage> resposta)
        {
            var ex = await Assert.ThrowsAsync<AsaasIntegracaoException>(
                () => Gateway(new HandlerFalso(resposta)).CriarTransferenciaPixAsync(Pedido));
            return ex.TipoErro;
        }

        private const string ErrosDescricao = "{\"errors\":[{\"code\":\"invalid_description\",\"description\":\"O campo description excede 140 caracteres\"}]}";
        private const string ErrosChavePix = "{\"errors\":[{\"code\":\"invalid_pixAddressKey\",\"description\":\"Chave Pix não encontrada\"}]}";
        private const string ErrosSaldo = "{\"errors\":[{\"code\":\"insufficient_balance\",\"description\":\"Saldo insuficiente\"}]}";

        // ── Sem resposta conclusiva: a transferência PODE existir ──────────────

        [Fact]
        public async Task Timeout_e_resultado_incerto()
        {
            var tipo = await ClassificacaoAsync(() => throw new TaskCanceledException("HttpClient.Timeout"));
            Assert.Equal(AsaasErroTipo.ResultadoIncerto, tipo);
        }

        [Fact]
        public async Task Falha_de_rede_e_resultado_incerto()
        {
            var tipo = await ClassificacaoAsync(() => throw new HttpRequestException("connection reset"));
            Assert.Equal(AsaasErroTipo.ResultadoIncerto, tipo);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.GatewayTimeout)]
        public async Task Status_5xx_e_resultado_incerto(HttpStatusCode status)
        {
            Assert.Equal(AsaasErroTipo.ResultadoIncerto, await ClassificacaoAsync(() => Resposta(status, ErrosDescricao)));
        }

        // 4xx que a documentação do endpoint NÃO descreve: sem evidência de que nada foi criado.
        [Theory]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        [InlineData(HttpStatusCode.Conflict)]
        [InlineData(HttpStatusCode.UnprocessableEntity)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        public async Task Status_4xx_nao_documentado_e_resultado_incerto(HttpStatusCode status)
        {
            Assert.Equal(AsaasErroTipo.ResultadoIncerto, await ClassificacaoAsync(() => Resposta(status, ErrosDescricao)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("<html>Bad Request</html>")]
        [InlineData("{}")]
        [InlineData("{\"errors\":[]}")]
        public async Task Status_400_sem_lista_de_erros_e_resultado_incerto(string corpo)
        {
            Assert.Equal(AsaasErroTipo.ResultadoIncerto,
                await ClassificacaoAsync(() => Resposta(HttpStatusCode.BadRequest, corpo)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("não é json")]
        [InlineData("{\"status\":\"PENDING\"}")]
        public async Task Resposta_2xx_sem_id_legivel_e_resultado_incerto(string corpo)
        {
            Assert.Equal(AsaasErroTipo.ResultadoIncerto, await ClassificacaoAsync(() => Resposta(HttpStatusCode.OK, corpo)));
        }

        // ── Rejeição comprovada (respostas documentadas): nada foi criado ──────

        // 400 documentado = rejeição comprovada (nunca ResultadoIncerto). O subtipo vem do
        // Classificar existente, que hoje casa "descri" com o NOME do campo "description"
        // presente em todo erro do Asaas — então todo 400 sai PayloadInvalido. Limitação
        // conhecida, fora deste achado: não duplica dinheiro (o Asaas recusou), mas retenta
        // a cada varredura. Ver ARQUITETURA.md, "Riscos remanescentes".
        [Theory]
        [InlineData(ErrosDescricao)]
        [InlineData(ErrosChavePix)]
        [InlineData(ErrosSaldo)]
        public async Task Status_400_com_lista_de_erros_e_rejeicao_comprovada(string corpo)
        {
            var tipo = await ClassificacaoAsync(() => Resposta(HttpStatusCode.BadRequest, corpo));
            Assert.NotEqual(AsaasErroTipo.ResultadoIncerto, tipo);
            Assert.Equal(AsaasIntegracaoException.Classificar(HttpStatusCode.BadRequest, corpo), tipo);
        }

        [Fact]
        public async Task Status_401_e_rejeicao_comprovada()
        {
            var corpo = "{\"errors\":[{\"code\":\"invalid_access_token\",\"description\":\"A chave de API fornecida é inválida\"}]}";
            Assert.Equal(AsaasErroTipo.ClienteError, await ClassificacaoAsync(() => Resposta(HttpStatusCode.Unauthorized, corpo)));
        }

        [Fact]
        public void Resultado_incerto_nunca_e_recuperavel_automaticamente()
        {
            Assert.False(AsaasErroTipo.ResultadoIncerto.EhRecuperavel());
        }

        // ── Sucesso ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Resposta_2xx_com_id_devolve_a_transferencia_e_chama_o_Asaas_uma_vez()
        {
            var handler = new HandlerFalso(() => Resposta(HttpStatusCode.OK, "{\"id\":\"tra_abc123\",\"status\":\"PENDING\"}"));
            var resposta = await Gateway(handler).CriarTransferenciaPixAsync(Pedido);
            Assert.Equal("tra_abc123", resposta.Id);
            Assert.Equal("PENDING", resposta.Status);
            Assert.Equal(1, handler.Chamadas);
        }

        // ── externalReference ──────────────────────────────────────────────────

        private static System.Text.Json.JsonElement CorpoEnviado(HandlerFalso handler)
            => System.Text.Json.JsonDocument.Parse(handler.UltimoCorpo!).RootElement;

        [Fact]
        public async Task Referencia_externa_vai_no_corpo_como_externalReference()
        {
            var handler = new HandlerFalso(() => Resposta(HttpStatusCode.OK, "{\"id\":\"tra_1\",\"status\":\"PENDING\"}"));
            var pagamentoId = Guid.NewGuid().ToString();

            await Gateway(handler).CriarTransferenciaPixAsync(Pedido with { ReferenciaExterna = pagamentoId });

            var corpo = CorpoEnviado(handler);
            Assert.Equal(pagamentoId, corpo.GetProperty("externalReference").GetString());
            // Os campos que já existiam continuam com os nomes da API.
            Assert.Equal(150, corpo.GetProperty("value").GetDouble());
            Assert.Equal("12345678909", corpo.GetProperty("pixAddressKey").GetString());
            Assert.Equal("CPF", corpo.GetProperty("pixAddressKeyType").GetString());
            Assert.Equal("Liberação contrato", corpo.GetProperty("description").GetString());
        }

        [Fact]
        public async Task Sem_referencia_externa_o_campo_nao_e_enviado()
        {
            var handler = new HandlerFalso(() => Resposta(HttpStatusCode.OK, "{\"id\":\"tra_1\",\"status\":\"PENDING\"}"));
            await Gateway(handler).CriarTransferenciaPixAsync(Pedido);
            Assert.False(CorpoEnviado(handler).TryGetProperty("externalReference", out _));
        }
    }
}
