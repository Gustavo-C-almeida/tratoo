using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Pagamentos
{
    public class PagamentoRepository : IPagamentoRepository
    {
        private readonly TratooContext _db;

        public PagamentoRepository(TratooContext db)
        {
            _db = db;
        }

        public async Task<Pagamento?> GetByIdAsync(Guid id)
        {
            return await _db.Pagamentos
                .Include(p => p.ContratoServico)
                    .ThenInclude(c => c!.Projeto)
                .Include(p => p.Ledger)
                .Include(p => p.Disputas)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<Pagamento?> GetByContratoServicoIdAsync(Guid contratoServicoId)
        {
            return await _db.Pagamentos
                .Include(p => p.ContratoServico)
                .Include(p => p.Ledger)
                .Include(p => p.Disputas)
                .FirstOrDefaultAsync(p => p.ContratoServicoId == contratoServicoId);
        }

        public async Task<Pagamento?> GetByGatewayIdAsync(string gatewayPagamentoId)
        {
            return await _db.Pagamentos
                .Include(p => p.ContratoServico)
                .Include(p => p.Ledger)
                .Include(p => p.Disputas)
                .FirstOrDefaultAsync(p => p.GatewayPagamentoId == gatewayPagamentoId);
        }

        public async Task<List<Pagamento>> GetPendentesLiberacaoAsync(DateTime ate)
        {
            return await _db.Pagamentos
                .Include(p => p.ContratoServico)
                    .ThenInclude(c => c!.Prestador)
                        .ThenInclude(pr => pr.ContaBancaria)
                .Where(p =>
                    p.Status == StatusPagamento.Retido &&
                    p.LiberacaoAutomaticaEm.HasValue &&
                    p.LiberacaoAutomaticaEm.Value <= ate)
                .ToListAsync();
        }

        public async Task AddAsync(Pagamento pagamento)
        {
            await _db.Pagamentos.AddAsync(pagamento);
        }

        public async Task AddLedgerAsync(LedgerFinanceiro entrada)
        {
            await _db.LedgerFinanceiro.AddAsync(entrada);
        }

        public async Task AddDisputaAsync(DisputaPagamento disputa)
        {
            await _db.DisputasPagamento.AddAsync(disputa);
        }

        public async Task AddWebhookLogAsync(WebhookLog log)
        {
            await _db.WebhookLogs.AddAsync(log);
        }

        public async Task<bool> WebhookJaProcessadoAsync(string chaveIdempotencia)
        {
            return await _db.WebhookLogs
                .AnyAsync(w => w.ChaveIdempotencia == chaveIdempotencia && w.ProcessadoComSucesso);
        }

        public async Task<DisputaPagamento?> GetDisputaAtivaAsync(Guid pagamentoId)
        {
            return await _db.DisputasPagamento
                .Where(d => d.PagamentoId == pagamentoId &&
                            d.Status != StatusDisputa.Encerrada &&
                            d.Status != StatusDisputa.ResolvidaAFavorContratante &&
                            d.Status != StatusDisputa.ResolvidaAFavorPrestador)
                .FirstOrDefaultAsync();
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
