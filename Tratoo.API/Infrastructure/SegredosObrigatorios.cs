using System.Security.Cryptography;
using System.Text;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Segredos que a API se recusa a rodar sem, fora de Development. Hoje: o token do webhook
    /// do Asaas (<c>Asaas:WebhookToken</c>, variável <c>Asaas__WebhookToken</c>).
    ///
    /// Antes, token vazio = webhook ABERTO: qualquer pessoa podia forjar um evento de
    /// "pagamento recebido". Agora, fora de Development, token ausente, com placeholder ou
    /// curto demais impede a subida (saída limpa, exit 1 — o deploy falha no health check e a
    /// versão anterior continua no ar) e o endpoint responde 401 se, por algum motivo, chegar
    /// a rodar sem ele. Em Development o token continua opcional.
    ///
    /// Durante uma troca do token, <c>Asaas:WebhookTokenAnterior</c> também é aceito
    /// (Docs/ROTACAO-CHAVES.md). A comparação é sempre em tempo constante.
    /// </summary>
    public static class SegredosObrigatorios
    {
        public const string WebhookTokenConfiguracao = "Asaas:WebhookToken";
        public const string WebhookTokenVariavel = "Asaas__WebhookToken";
        public const string WebhookTokenAnteriorConfiguracao = "Asaas:WebhookTokenAnterior";
        public const string WebhookTokenAnteriorVariavel = "Asaas__WebhookTokenAnterior";

        /// <summary>Mínimo de caracteres do token (gerar com <c>openssl rand -base64 32</c>).</summary>
        public const int TamanhoMinimoWebhookToken = 16;

        private const string PrefixoPlaceholder = "DEFINA_";

        /// <summary>
        /// Lança <see cref="InvalidOperationException"/> (mensagem sem o valor do segredo) se o
        /// token estiver ausente ou inválido fora de Development. O anterior, se informado,
        /// passa pelas mesmas regras e não pode ser igual ao atual.
        /// </summary>
        public static void ValidarWebhookAsaas(string? token, string? tokenAnterior, bool emDevelopment)
        {
            if (emDevelopment)
                return;

            ValidarToken(token, WebhookTokenVariavel);

            if (string.IsNullOrEmpty(tokenAnterior))
                return;

            ValidarToken(tokenAnterior, WebhookTokenAnteriorVariavel);
            if (TokensIguais(token!, tokenAnterior))
                throw new InvalidOperationException(
                    $"{WebhookTokenAnteriorVariavel} é igual a {WebhookTokenVariavel}. Numa troca, " +
                    $"{WebhookTokenVariavel} é o token NOVO e {WebhookTokenAnteriorVariavel} o antigo; " +
                    $"fora de uma troca, remova {WebhookTokenAnteriorVariavel}.");
        }

        private static void ValidarToken(string? token, string variavel)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    $"Token do webhook do Asaas ausente: defina {variavel}. Sem ele o webhook ficaria aberto " +
                    "a eventos forjados (ex.: pagamento recebido). Use o mesmo valor do painel do Asaas " +
                    "(Webhooks → Token de autenticação).");

            if (token.StartsWith(PrefixoPlaceholder, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{variavel} ainda está com o placeholder do arquivo de exemplo. Defina um token real.");

            if (token.Length < TamanhoMinimoWebhookToken)
                throw new InvalidOperationException(
                    $"{variavel} tem menos de {TamanhoMinimoWebhookToken} caracteres. " +
                    "Gere um com `openssl rand -base64 32`.");
        }

        /// <summary>
        /// true se o token recebido confere com o atual ou, durante uma troca, com o anterior.
        /// Os DOIS são sempre comparados (sem curto-circuito entre eles). Se nenhum token
        /// estiver configurado, só aceita quando <paramref name="emDevelopment"/> — fora dele,
        /// webhook sem segredo é recusado.
        /// </summary>
        public static bool WebhookTokenConfere(
            string? recebido, string? atual, string? anterior, bool emDevelopment)
        {
            var temAtual = !string.IsNullOrWhiteSpace(atual);
            var temAnterior = !string.IsNullOrWhiteSpace(anterior);

            if (!temAtual && !temAnterior)
                return emDevelopment;

            if (string.IsNullOrEmpty(recebido))
                return false;

            var bateAtual = temAtual && TokensIguais(recebido, atual!);
            var bateAnterior = temAnterior && TokensIguais(recebido, anterior!);
            return bateAtual | bateAnterior;
        }

        private static bool TokensIguais(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
