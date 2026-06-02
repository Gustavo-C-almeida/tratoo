
namespace Tratoo.Domain.Features.Pagamentos
{
    /// <summary>
    /// Abstração do gateway de pagamento.
    /// Desacopla o domínio de implementação concreta (Asaas).
    /// Para trocar de provedor, basta implementar esta interface.
    /// </summary>
    public interface IAsaasGatewayService
    {
        /// <summary>
        /// Cria ou recupera o cliente no gateway usando o externalReference do usuário.
        /// Retorna o ID do cliente no gateway (cus_...).
        /// </summary>
        Task<string> CriarOuObterClienteAsync(AsaasClienteRequest request);

        /// <summary>
        /// Cria uma cobrança PIX no gateway.
        /// Retorna o ID e status da cobrança.
        /// </summary>
        Task<AsaasCobrancaResponse> CriarCobrancaPixAsync(AsaasCobrancaRequest request);

        /// <summary>
        /// Recupera o QR Code PIX de uma cobrança existente.
        /// Retorna o payload e a imagem base64.
        /// </summary>
        Task<AsaasPixQrCodeResponse> ObterQrCodePixAsync(string cobrancaId);

        /// <summary>
        /// Cria uma transferência PIX ao prestador.
        /// Retorna o ID e status da transferência.
        /// </summary>
        Task<AsaasTransferenciaResponse> CriarTransferenciaPixAsync(AsaasTransferenciaPixRequest request);

        /// <summary>
        /// Solicita estorno total ou parcial de uma cobrança.
        /// </summary>
        Task EstornarCobrancaAsync(string cobrancaId, decimal? valorParcial = null);

        /// <summary>
        /// Consulta o status atual de uma cobrança no gateway.
        /// Retorna a string de status do gateway (PENDING, RECEIVED, etc.).
        /// </summary>
        Task<string> ObterStatusCobrancaAsync(string cobrancaId);

        /// <summary>
        /// Simula o recebimento de um pagamento PIX no sandbox do Asaas.
        /// Equivale a clicar em "Simular Recebimento" no painel sandbox.
        /// USE SOMENTE EM AMBIENTE DE DESENVOLVIMENTO/SANDBOX.
        /// </summary>
        Task SimularPagamentoAsync(string cobrancaId, decimal valor);
    }
}
