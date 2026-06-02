
namespace Tratoo.Domain.Features.Pagamentos
{
    public interface IPagamentoService
    {
        /// <summary>
        /// Inicia o pagamento de um contrato ativo: cria o registro de pagamento,
        /// gera a cobrança PIX no gateway e retorna o QR Code ao contratante.
        /// </summary>
        Task<PagamentoPixDto> IniciarPagamentoAsync(Guid contratoServicoId, int contratanteId);

        /// <summary>
        /// Retorna o detalhe de um pagamento, incluindo ledger e disputas.
        /// Lança NegocioException se o usuário não for parte do contrato.
        /// </summary>
        Task<PagamentoDetalheDto> ObterDetalheAsync(Guid pagamentoId, int usuarioId);

        /// <summary>
        /// Retorna os dados PIX (QR Code) de um pagamento aguardando pagamento.
        /// Atualiza o QR Code no banco se ele tiver expirado.
        /// </summary>
        Task<PagamentoPixDto> ObterPixAsync(Guid pagamentoId, int usuarioId);

        /// <summary>
        /// Processa um evento de webhook recebido do Asaas.
        /// Garante idempotência — ignora eventos já processados.
        /// </summary>
        Task ProcessarWebhookAsync(string tipoEvento, string payloadJson);

        /// <summary>
        /// Contratante aprova a entrega e libera o escrow ao prestador.
        /// Dispara a transferência PIX imediata.
        /// </summary>
        Task<PagamentoResumoDto> LiberarPagamentoAsync(Guid pagamentoId, int contratanteId, string? observacao);

        /// <summary>
        /// Libera automaticamente um pagamento (chamado pelo BackgroundService).
        /// Usado quando o contratante não aprova dentro do prazo.
        /// </summary>
        Task LiberarAutomaticamenteAsync(Guid pagamentoId);

        /// <summary>
        /// Abre uma disputa sobre um pagamento retido.
        /// Contratante ou prestador pode abrir disputa.
        /// </summary>
        Task<DisputaResumoDto> AbrirDisputaAsync(Guid pagamentoId, int usuarioId, AbrirDisputaDto dto);

        /// <summary>
        /// Resolve uma disputa (uso exclusivo de administradores da plataforma).
        /// </summary>
        Task ResolverDisputaAsync(Guid pagamentoId, Guid disputaId, int adminId, ResolverDisputaDto dto);

        /// <summary>
        /// Solicita estorno de um pagamento (antes da liberação ao prestador).
        /// </summary>
        Task SolicitarEstornoAsync(Guid pagamentoId, int contratanteId);

        /// <summary>
        /// Processa o reembolso ao cancelar um contrato ativo com escrow retido.
        /// Cobra taxa administrativa de 5% e reembolsa 95% ao contratante.
        /// Não faz nada se não houver pagamento retido para o contrato.
        /// </summary>
        Task EstornarCancelamentoContratoAsync(Guid contratoServicoId, int usuarioId);

        /// <summary>
        /// Simula o recebimento do pagamento PIX diretamente via API do Asaas Sandbox.
        /// Substitui o webhook PAYMENT_RECEIVED em ambiente localhost.
        /// USE SOMENTE EM DESENVOLVIMENTO — nunca em produção.
        /// </summary>
        Task<PagamentoResumoDto> SimularConfirmacaoAsync(Guid pagamentoId, int usuarioId);

        /// <summary>
        /// Consulta o status atual da cobrança no Asaas e sincroniza com o banco local.
        /// Alternativa ao webhook para ambientes sem URL pública (localhost).
        /// </summary>
        Task<PagamentoResumoDto> SincronizarStatusAsync(Guid pagamentoId, int usuarioId);
    }
}
