using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public class EntregaRepository : IEntregaRepository
    {
        private readonly TratooContext _ctx;

        public EntregaRepository(TratooContext ctx) => _ctx = ctx;

        public async Task AddAsync(Entrega entrega)
            => await _ctx.Entregas.AddAsync(entrega);

        public async Task<Entrega?> GetAtualPorContratoAsync(Guid contratoServicoId)
            => await _ctx.Entregas
                .Include(e => e.Anexos)
                .Include(e => e.Links)
                .Where(e => e.ContratoServicoId == contratoServicoId)
                .OrderByDescending(e => e.CriadoEm)
                .AsSplitQuery()
                .FirstOrDefaultAsync();

        public async Task<Entrega?> GetPendentePorContratoAsync(Guid contratoServicoId)
            => await _ctx.Entregas
                .Include(e => e.Anexos)
                .Include(e => e.Links)
                .Where(e => e.ContratoServicoId == contratoServicoId
                         && e.Status == EntregaStatus.PendenteAprovacao)
                .OrderByDescending(e => e.CriadoEm)
                .AsSplitQuery()
                .FirstOrDefaultAsync();

        public async Task<EntregaAnexo?> GetAnexoAsync(Guid anexoId)
            => await _ctx.EntregaAnexos
                .Include(a => a.Entrega)
                .FirstOrDefaultAsync(a => a.Id == anexoId);

        public async Task AddHistoricoAsync(HistoricoContrato historico)
            => await _ctx.HistoricosContrato.AddAsync(historico);

        public async Task<List<HistoricoContrato>> GetHistoricoPorContratoAsync(Guid contratoServicoId)
            => await _ctx.HistoricosContrato
                .AsNoTracking()
                .Where(h => h.ContratoServicoId == contratoServicoId)
                .OrderByDescending(h => h.DataEvento)
                .ToListAsync();

        public async Task SaveChangesAsync()
            => await _ctx.SaveChangesAsync();
    }
}
