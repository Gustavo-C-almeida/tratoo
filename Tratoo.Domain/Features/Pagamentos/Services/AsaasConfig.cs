namespace Tratoo.Domain.Features.Pagamentos
{
    public class AsaasConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://sandbox.asaas.com/api/v3";

        /// <summary>
        /// Token compartilhado configurado no painel do Asaas (Webhooks → Token de autenticação).
        /// O Asaas envia este valor no header "asaas-access-token" de cada notificação.
        /// O endpoint de webhook rejeita (401) requisições cujo token não confira.
        /// </summary>
        public string WebhookToken { get; set; } = string.Empty;

        /// <summary>
        /// Quando true, a liberação ao prestador é finalizada (status Liberado) imediatamente
        /// após a criação da transferência, sem aguardar o webhook TRANSFER_DONE. Use apenas
        /// em ambientes sem entrega de webhook (ex.: localhost sem túnel). Padrão: false —
        /// fluxo dirigido por webhook (recomendado para sandbox com webhooks ativos e produção).
        /// </summary>
        public bool ConfirmarTransferenciaImediatamente { get; set; } = false;

        /// <summary>
        /// Percentual estimado da taxa operacional do gateway (Asaas) exibido ao usuário
        /// para transparência. Valor informativo — não é usado em nenhum cálculo financeiro.
        /// Ex: 0.0199 = 1,99%. Configurável para refletir mudanças tarifárias sem deploy de código.
        /// </summary>
        public decimal TaxaOperacionalEstimadaPercentual { get; set; } = 0.0199m;
    }
}
