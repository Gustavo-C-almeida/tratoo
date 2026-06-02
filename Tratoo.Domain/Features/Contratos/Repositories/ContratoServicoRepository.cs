using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public class ContratoServicoRepository : IContratoServicoRepository
    {
        private readonly TratooContext _ctx;

        public ContratoServicoRepository(TratooContext ctx) => _ctx = ctx;

        public async Task<ContratoServico?> GetByIdAsync(Guid id)
            => await _ctx.ContratosServico
                .Include(c => c.Projeto)
                .Include(c => c.Contratante)
                .Include(c => c.Prestador)
                .Include(c => c.Snapshot)
                .FirstOrDefaultAsync(c => c.Id == id);

        public async Task<ContratoServico?> GetByPropostaIdAsync(Guid propostaId)
            => await _ctx.ContratosServico
                .Include(c => c.Projeto)
                .FirstOrDefaultAsync(c => c.PropostaId == propostaId);

        public async Task<List<ContratoServico>> GetDoContratanteAsync(int contratanteId)
            => await _ctx.ContratosServico
                .AsNoTracking()
                .Include(c => c.Projeto)
                .Include(c => c.Prestador)
                .Where(c => c.ContratanteId == contratanteId)
                .OrderByDescending(c => c.CriadoEm)
                .AsSplitQuery()
                .ToListAsync();

        public async Task<List<ContratoServico>> GetDoPrestadorAsync(int prestadorId)
            => await _ctx.ContratosServico
                .AsNoTracking()
                .Include(c => c.Projeto)
                .Include(c => c.Contratante)
                .Where(c => c.PrestadorId == prestadorId)
                .OrderByDescending(c => c.CriadoEm)
                .AsSplitQuery()
                .ToListAsync();

        public async Task<List<ContratoServico>> GetExpiradosAsync(DateTime agora)
            => await _ctx.ContratosServico
                .Where(c =>
                    (c.Status == ContratoServicoStatus.Gerado ||
                     c.Status == ContratoServicoStatus.AguardandoAssinatura) &&
                    c.ExpiraEm < agora)
                .ToListAsync();

        public async Task AddAsync(ContratoServico contrato)
            => await _ctx.ContratosServico.AddAsync(contrato);

        public async Task AddSnapshotAsync(ContratoSnapshot snapshot)
            => await _ctx.ContratoSnapshots.AddAsync(snapshot);

        public async Task SaveChangesAsync()
            => await _ctx.SaveChangesAsync();
    }
}
