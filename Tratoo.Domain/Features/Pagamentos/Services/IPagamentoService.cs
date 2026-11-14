
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
        /// Dispara a transferência PIX imediata. <paramref name="ip"/> e
        /// <paramref name="userAgent"/> são auditados (ação sensível — antifraude).
        /// </summary>
        Task<PagamentoResumoDto> LiberarPagamentoAsync(
            Guid pagamentoId, int contratanteId, string? observacao, string? ip, string? userAgent);

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
        /// Atualiza pagamento e contrato e grava trilha de auditoria com estado anterior/posterior.
        /// </summary>
        Task ResolverDisputaAsync(Guid pagamentoId, Guid disputaId, int adminId, ResolverDisputaDto dto, string ip = "admin");

        /// <summary>
        /// Solicita estorno de um pagamento (antes da liberação ao prestador).
        /// </summary>
        Task SolicitarEstornoAsync(Guid pagamentoId, int contratanteId);

        /// <summary>
        /// Fase 1 do reembolso por cancelamento (achado A1), chamada DENTRO da transação do
        /// cancelamento: reserva de forma atômica o pagamento Retido do contrato
        /// (Retido → EstornoEmProgresso), o que exclui a liberação (manual ou do job), a disputa
        /// e outro estorno. Recusa (NegocioException) se o pagamento já estiver em transferência,
        /// liberado, em disputa ou em estorno. Devolve null se não há valor retido a estornar.
        /// Não chama o gateway.
        /// </summary>
        Task<Guid?> ReservarEstornoParaCancelamentoAsync(Guid contratoServicoId);

        /// <summary>
        /// Fase 2 de um estorno reservado: chama o Asaas FORA de qualquer transação e registra o
        /// desfecho (status + ledger na mesma transação). Nunca devolve o pagamento a Retido:
        /// em <see cref="ResultadoEstorno.FalhaDefinitiva"/> o chamador decide desfazer a reserva.
        /// </summary>
        Task<ResultadoEstorno> EstornarReservadoAsync(Guid pagamentoId, int usuarioId, string motivo);

        /// <summary>
        /// Desfaz a reserva (EstornoEmProgresso → Retido) depois de falha DEFINITIVA do gateway e
        /// adiciona a entrada de ledger. Não grava: o chamador faz SaveChanges e o commit na
        /// mesma transação em que desfaz o resto.
        /// </summary>
        Task<bool> ReverterReservaEstornoAsync(Guid pagamentoId, int usuarioId, string motivo);

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

        /// <summary>
        /// Reprocessa manualmente uma transferência que falhou (status FalhaTransferencia).
        /// Uso exclusivo de administradores. Reverte para Retido e dispara nova tentativa.
        /// </summary>
        Task<PagamentoResumoDto> ReprocessarTransferenciaAsync(Guid pagamentoId, int adminId);
    }
}
