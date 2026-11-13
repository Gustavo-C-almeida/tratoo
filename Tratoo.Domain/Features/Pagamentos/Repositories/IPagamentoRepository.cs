using Microsoft.EntityFrameworkCore.Storage;
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
        /// <summary>
        /// Antecipa para <paramref name="agora"/> a liberação automática do pagamento RETIDO do
        /// contrato (o job de liberação passa a enxergá-lo). Usado na aprovação da entrega, dentro
        /// da mesma transação: se a liberação imediata falhar ou o processo cair, a liberação já
        /// está pendente de forma durável. Retorna as linhas afetadas.
        /// </summary>
        Task<int> AnteciparLiberacaoAutomaticaAsync(Guid contratoServicoId, DateTime agora);

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
        Task<WebhookLog?> GetWebhookLogAsync(string chaveIdempotencia);

        /// <summary>
        /// Transição atômica para Retido, a partir de Aguardando ou Processando. Devolve false
        /// se outro processo já confirmou o pagamento.
        /// </summary>
        Task<bool> TryMarcarRetidoAsync(Guid pagamentoId);

        Task<IDbContextTransaction> IniciarTransacaoAsync();

        /// <summary>Descarta o estado em memória do pagamento e o relê do banco.</summary>
        Task RecarregarAsync(Pagamento pagamento);
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
