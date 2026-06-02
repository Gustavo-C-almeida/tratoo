using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Pagamentos
{
    public interface IPagamentoRepository
    {
        Task<Pagamento?> GetByIdAsync(Guid id);
        Task<Pagamento?> GetByContratoServicoIdAsync(Guid contratoServicoId);
        Task<Pagamento?> GetByGatewayIdAsync(string gatewayPagamentoId);
        Task<List<Pagamento>> GetPendentesLiberacaoAsync(DateTime ate);
        Task AddAsync(Pagamento pagamento);
        Task AddLedgerAsync(LedgerFinanceiro entrada);
        Task AddDisputaAsync(DisputaPagamento disputa);
        Task AddWebhookLogAsync(WebhookLog log);
        Task<bool> WebhookJaProcessadoAsync(string chaveIdempotencia);
        Task<DisputaPagamento?> GetDisputaAtivaAsync(Guid pagamentoId);
        Task SaveChangesAsync();
    }
}
