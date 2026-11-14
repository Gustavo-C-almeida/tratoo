using Microsoft.EntityFrameworkCore.Storage;
using Tratoo.Domain.Enums;
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

        // ── Estorno com reserva (achado A1) ─────────────────────────────────
        // O estorno segue o mesmo padrão da liberação: reserva condicional no banco, chamada ao
        // Asaas FORA de qualquer transação e só então a finalização. A reserva
        // (Retido → EstornoEmProgresso) e a reivindicação da liberação
        // (Retido → TransferenciaEmProgresso) partem do MESMO estado: no máximo uma vence.

        /// <summary>Pagamentos do contrato que ainda contam (não Cancelado/Falhou/Estornado), sem rastrear.</summary>
        Task<List<Pagamento>> ListarVigentesPorContratoAsync(Guid contratoServicoId);

        /// <summary>Transição atômica Retido → EstornoEmProgresso. True se este chamador reservou.</summary>
        Task<bool> TryReservarEstornoAsync(Guid pagamentoId);

        /// <summary>EstornoEmProgresso → Estornado (o Asaas aceitou o estorno). True se aplicou.</summary>
        Task<bool> TryConcluirEstornoAsync(Guid pagamentoId, DateTime agora);

        /// <summary>
        /// EstornoEmProgresso → Retido. Só para falha DEFINITIVA do Asaas (nada foi estornado).
        /// Nunca usar com resultado incerto.
        /// </summary>
        Task<bool> TryReverterReservaEstornoAsync(Guid pagamentoId);

        /// <summary>Marca (StatusGateway) um estorno reservado cujo resultado no Asaas é incerto.</summary>
        Task<bool> MarcarEstornoIncertoAsync(Guid pagamentoId, string marcador);

        /// <summary>Transição atômica Retido → EmDisputa. True se aplicou.</summary>
        Task<bool> TryMarcarEmDisputaAsync(Guid pagamentoId);

        /// <summary>Trava a linha do pagamento (SELECT ... FOR UPDATE) até o fim da transação.</summary>
        Task BloquearParaAtualizacaoAsync(Guid pagamentoId);

        /// <summary>Status atual no banco, ignorando o que estiver rastreado em memória.</summary>
        Task<StatusPagamento?> ObterStatusAsync(Guid pagamentoId);

        /// <summary>
        /// Confirmação de estorno vinda do gateway: → Estornado, só a partir dos estados em que
        /// nenhum valor saiu para o prestador. True se aplicou.
        /// </summary>
        Task<bool> TryConfirmarEstornoAsync(Guid pagamentoId, DateTime agora, IReadOnlyCollection<StatusPagamento> origens);

        Task<bool> ExisteLedgerAsync(Guid pagamentoId, TipoEntradaLedger tipo);

        /// <summary>
        /// Solta (detach) uma entidade que não chegou a ser gravada — depois de um SaveChanges que
        /// falhou —, para que um SaveChanges posterior no mesmo escopo não a grave fora da
        /// transação a que pertencia.
        /// </summary>
        void Descartar(object entidade);

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
