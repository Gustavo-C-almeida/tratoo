using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Tratoo.Domain.Features.Pagamentos;
using Microsoft.AspNetCore.TestHost;
using Tratoo.API.EndPoints;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Features.Shared;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// O token do webhook do Asaas é obrigatório fora de Development: antes, token vazio
    /// deixava o endpoint ABERTO a eventos forjados. Cobre a regra (unidade), o endpoint real
    /// e a subida do processo.
    /// </summary>
    public class WebhookAsaasObrigatorioTests
    {
        private const string Token = "token-do-webhook-com-mais-de-16-caracteres";
        private const string Outro = "outro-token-do-webhook-tambem-longo-123";

        // ── Regra de validação na subida ─────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("DEFINA_TOKEN_DO_WEBHOOK_COM_16_OU_MAIS_CARACTERES")]
        [InlineData("curto")]
        public void Fora_de_Development_token_ausente_ou_invalido_lanca(string? token)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                SegredosObrigatorios.ValidarWebhookAsaas(token, null, emDevelopment: false));

            Assert.Contains(SegredosObrigatorios.WebhookTokenVariavel, ex.Message);
            if (!string.IsNullOrWhiteSpace(token))
                Assert.DoesNotContain(token, ex.Message); // a mensagem nunca repete o segredo
        }

        [Fact]
        public void Token_valido_passa() =>
            SegredosObrigatorios.ValidarWebhookAsaas(Token, null, emDevelopment: false);

        [Fact]
        public void Em_Development_o_token_e_opcional() =>
            SegredosObrigatorios.ValidarWebhookAsaas(null, null, emDevelopment: true);

        [Fact]
        public void Anterior_valido_e_diferente_passa() =>
            SegredosObrigatorios.ValidarWebhookAsaas(Token, Outro, emDevelopment: false);

        [Fact]
        public void Anterior_igual_ao_atual_lanca()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                SegredosObrigatorios.ValidarWebhookAsaas(Token, Token, emDevelopment: false));
            Assert.Contains(SegredosObrigatorios.WebhookTokenAnteriorVariavel, ex.Message);
        }

        [Fact]
        public void Anterior_curto_lanca() =>
            Assert.Throws<InvalidOperationException>(() =>
                SegredosObrigatorios.ValidarWebhookAsaas(Token, "curto", emDevelopment: false));

        // ── Comparação do token recebido ─────────────────────────────────────

        [Theory]
        [InlineData(Token, Token, null, false, true)]       // confere
        [InlineData(Outro, Token, null, false, false)]      // errado
        [InlineData(null, Token, null, false, false)]       // header ausente
        [InlineData("", Token, null, false, false)]
        [InlineData(Outro, Token, Outro, false, true)]      // durante a troca: o anterior vale
        [InlineData(Token, Token, Outro, false, true)]      // e o novo também
        [InlineData("terceiro", Token, Outro, false, false)]
        [InlineData(null, null, null, false, false)]        // sem segredo em Production: recusa
        [InlineData("qualquer", "", null, false, false)]
        [InlineData(null, null, null, true, true)]          // sem segredo em Development: aceita
        [InlineData("qualquer", null, null, true, true)]
        [InlineData(Outro, Token, null, true, false)]       // com segredo, até Development confere
        public void Confere_o_token_recebido(
            string? recebido, string? atual, string? anterior, bool dev, bool esperado) =>
            Assert.Equal(esperado, SegredosObrigatorios.WebhookTokenConfere(recebido, atual, anterior, dev));

        // ── Endpoint real ────────────────────────────────────────────────────

        [Fact]
        public async Task Endpoint_em_Production_sem_token_configurado_responde_401()
        {
            await using var app = TestHostFactory.Criar(
                isDevelopment: false,
                mapearRotas: a => a.AddEndPointWebhookAsaas(),
                configurarServicos: s => s.AddSingleton(
                    DispatchProxy.Create<IPagamentoService, ServicoQueNaoDeveSerChamado>()));
            await app.StartAsync();
            using var cliente = app.GetTestClient();

            var resposta = await cliente.PostAsync(
                "/api/webhooks/asaas", new StringContent("{\"event\":\"PAYMENT_RECEIVED\"}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);

            // Mesmo mandando um header, sem segredo configurado nada passa.
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/asaas")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.Add("asaas-access-token", "qualquer");
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.SendAsync(req)).StatusCode);
        }

        /// <summary>Stub: qualquer chamada falha — o 401 tem de vir ANTES de tocar no serviço.</summary>
        public class ServicoQueNaoDeveSerChamado : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? metodo, object?[]? args) =>
                throw new InvalidOperationException($"O serviço não podia ser chamado: {metodo?.Name}");
        }

        // ── Processo real ────────────────────────────────────────────────────

        private static Dictionary<string, string?> AmbienteProcesso(string ambiente, string? token)
        {
            var conexao = $"Host=127.0.0.1;Port={ProcessoApi.PortaLivre()};Database=x;Username=x;Password=x;Timeout=3";
            var env = new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = ambiente,
                ["PORT"] = ProcessoApi.PortaLivre().ToString(),
                ["ConnectionStrings__DefaultConnection"] = conexao,
                ["ConnectionStrings__VectorConnection"] = conexao,
                [DataProtector.VariavelDeAmbiente] = "uma-chave-de-teste-qualquer-0123456789"
            };
            if (token is not null)
                env[SegredosObrigatorios.WebhookTokenVariavel] = token;
            return env;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("curto")]
        [InlineData("DEFINA_TOKEN_DO_WEBHOOK_COM_16_OU_MAIS_CARACTERES")]
        public async Task Em_Production_sem_token_valido_a_API_nao_sobe(string? token)
        {
            var (exit, saida) = await ProcessoApi.RodarAsync(null, AmbienteProcesso("Production", token));

            Assert.Equal(1, exit);
            Assert.Contains("[startup]", saida);
            Assert.Contains(SegredosObrigatorios.WebhookTokenVariavel, saida);
            Assert.DoesNotContain("Unhandled exception", saida);
            Assert.DoesNotContain("Now listening", saida);
        }

        [Fact]
        public async Task Em_Production_com_token_valido_a_subida_passa_da_verificacao()
        {
            var (_, saida) = await ProcessoApi.RodarAsync(null, AmbienteProcesso("Production", Token));
            Assert.DoesNotContain("Token do webhook do Asaas", saida);
        }

        [Fact]
        public async Task Em_Development_sem_token_a_subida_passa_da_verificacao()
        {
            var (_, saida) = await ProcessoApi.RodarAsync(null, AmbienteProcesso("Development", null));
            Assert.DoesNotContain("Token do webhook do Asaas", saida);
        }
    }
}
