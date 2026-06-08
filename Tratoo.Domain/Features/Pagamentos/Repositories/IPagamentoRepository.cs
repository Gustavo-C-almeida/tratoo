using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Pagamentos
{
    public interface IPagamentoRepository
    {
        Task<Pagamento?> GetByIdAsync(Guid id);
        Task<Pagamento?> GetByContratoServicoIdAsync(Guid contratoServicoId);
        Task<Pagamento?> GetByGatewayIdAsync(string gatewayPagamentoId);
        Task<Pagamento?> GetByTransferenciaIdAsync(string asaasTransferenciaId);

        /// <summary>
        /// Transição atômica Retido → TransferenciaEmProgresso direto no banco (UPDATE condicional).
        /// Garante que apenas um chamador consiga "reivindicar" o pagamento para liberação,
        /// impedindo transferências duplicadas em corridas (aprovação manual + liberação automática).
        /// Retorna true se este chamador venceu a corrida.
        /// </summary>
        Task<bool> TryMarcarTransferenciaEmProgressoAsync(Guid pagamentoId);

        /// <summary>Reverte TransferenciaEmProgresso → Retido (usado se a criação da transferência falhar).</summary>
        Task ReverterTransferenciaParaRetidoAsync(Guid pagamentoId);
        Task<List<Pagamento>> GetPendentesLiberacaoAsync(DateTime ate);

        /// <summary>
        /// Soma o valor bruto dos pagamentos do contratante criados no dia (UTC) que
        /// representam dinheiro comprometido (exclui Cancelado, Falhou e Estornado).
        /// Usado no limite operacional diário (AML).
        /// </summary>
        Task<decimal> GetTotalIniciadoNoDiaPorContratanteAsync(int contratanteId, DateTime diaUtc);
        Task AddAsync(Pagamento pagamento);
        Task AddLedgerAsync(LedgerFinanceiro entrada);
        Task AddDisputaAsync(DisputaPagamento disputa);
        Task AddWebhookLogAsync(WebhookLog log);
        Task<bool> WebhookJaProcessadoAsync(string chaveIdempotencia);
        Task<DisputaPagamento?> GetDisputaAtivaAsync(Guid pagamentoId);

        /// <summary>Registra uma entrada de auditoria no histórico do contrato.</summary>
        Task AddHistoricoContratoAsync(HistoricoContrato historico);

        /// <summary>
        /// Lista todas as disputas (admin), com pagamento, contrato, projeto e partes.
        /// Filtros opcionais: status, intervalo de datas, contratante, prestador, projeto.
        /// </summary>
        Task<List<DisputaPagamento>> GetTodasDisputasAsync(
            StatusDisputa? status, DateTime? de, DateTime? ate,
            int? contratanteId, int? prestadorId, int? projetoId);

        /// <summary>Carrega uma disputa com todo o contexto (pagamento, contrato, projeto, partes).</summary>
        Task<DisputaPagamento?> GetDisputaComContextoAsync(Guid disputaId);

        /// <summary>Histórico de eventos do contrato (auditoria), ordenado cronologicamente.</summary>
        Task<List<HistoricoContrato>> GetHistoricoContratoAsync(Guid contratoServicoId);

        Task SaveChangesAsync();
    }
}
