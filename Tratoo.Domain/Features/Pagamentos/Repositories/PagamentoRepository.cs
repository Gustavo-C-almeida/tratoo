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

        public async Task<Pagamento?> GetByTransferenciaIdAsync(string asaasTransferenciaId)
        {
            return await _db.Pagamentos
                .Include(p => p.ContratoServico)
                    .ThenInclude(c => c!.Projeto)
                .Include(p => p.Ledger)
                .Include(p => p.Disputas)
                .FirstOrDefaultAsync(p => p.AsaasTransferenciaId == asaasTransferenciaId);
        }

        public async Task<bool> TryMarcarTransferenciaEmProgressoAsync(Guid pagamentoId)
        {
            // UPDATE ... WHERE Id = @id AND Status = 'Retido'. Atômico no banco:
            // apenas uma transição é aplicada mesmo sob requisições concorrentes.
            var linhas = await _db.Pagamentos
                .Where(p => p.Id == pagamentoId && p.Status == StatusPagamento.Retido)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, StatusPagamento.TransferenciaEmProgresso));
            return linhas == 1;
        }

        public async Task ReverterTransferenciaParaRetidoAsync(Guid pagamentoId)
        {
            await _db.Pagamentos
                .Where(p => p.Id == pagamentoId && p.Status == StatusPagamento.TransferenciaEmProgresso)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, StatusPagamento.Retido));
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

        public async Task<decimal> GetTotalIniciadoNoDiaPorContratanteAsync(int contratanteId, DateTime diaUtc)
        {
            var inicio = diaUtc.Date;
            var fim = inicio.AddDays(1);

            return await _db.Pagamentos
                .Where(p =>
                    p.ContratoServico != null &&
                    p.ContratoServico.ContratanteId == contratanteId &&
                    p.CriadoEm >= inicio && p.CriadoEm < fim &&
                    p.Status != StatusPagamento.Cancelado &&
                    p.Status != StatusPagamento.Falhou &&
                    p.Status != StatusPagamento.Estornado)
                .SumAsync(p => p.ValorBruto);
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

        public async Task AddHistoricoContratoAsync(HistoricoContrato historico)
        {
            await _db.HistoricosContrato.AddAsync(historico);
        }

        public async Task<List<DisputaPagamento>> GetTodasDisputasAsync(
            StatusDisputa? status, DateTime? de, DateTime? ate,
            int? contratanteId, int? prestadorId, int? projetoId)
        {
            var query = _db.DisputasPagamento
                .Include(d => d.Pagamento)
                    .ThenInclude(p => p.ContratoServico)
                        .ThenInclude(c => c!.Projeto)
                .Include(d => d.Pagamento)
                    .ThenInclude(p => p.ContratoServico)
                        .ThenInclude(c => c!.Contratante)
                .Include(d => d.Pagamento)
                    .ThenInclude(p => p.ContratoServico)
                        .ThenInclude(c => c!.Prestador)
                .AsNoTracking()
                .AsQueryable();

            if (status.HasValue)
                query = query.Where(d => d.Status == status.Value);
            if (de.HasValue)
                query = query.Where(d => d.AbertoEm >= de.Value);
            if (ate.HasValue)
                query = query.Where(d => d.AbertoEm < ate.Value);
            if (contratanteId.HasValue)
                query = query.Where(d => d.Pagamento.ContratoServico!.ContratanteId == contratanteId.Value);
            if (prestadorId.HasValue)
                query = query.Where(d => d.Pagamento.ContratoServico!.PrestadorId == prestadorId.Value);
            if (projetoId.HasValue)
                query = query.Where(d => d.Pagamento.ContratoServico!.ProjetoId == projetoId.Value);

            return await query
                .OrderByDescending(d => d.AbertoEm)
                .ToListAsync();
        }

        public async Task<DisputaPagamento?> GetDisputaComContextoAsync(Guid disputaId)
        {
            return await _db.DisputasPagamento
                .Include(d => d.Pagamento)
                    .ThenInclude(p => p.ContratoServico)
                        .ThenInclude(c => c!.Projeto)
                .Include(d => d.Pagamento)
                    .ThenInclude(p => p.ContratoServico)
                        .ThenInclude(c => c!.Contratante)
                .Include(d => d.Pagamento)
                    .ThenInclude(p => p.ContratoServico)
                        .ThenInclude(c => c!.Prestador)
                .Include(d => d.Pagamento)
                    .ThenInclude(p => p.Ledger)
                .FirstOrDefaultAsync(d => d.Id == disputaId);
        }

        public async Task<List<HistoricoContrato>> GetHistoricoContratoAsync(Guid contratoServicoId)
        {
            return await _db.HistoricosContrato
                .AsNoTracking()
                .Where(h => h.ContratoServicoId == contratoServicoId)
                .OrderBy(h => h.DataEvento)
                .ToListAsync();
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
