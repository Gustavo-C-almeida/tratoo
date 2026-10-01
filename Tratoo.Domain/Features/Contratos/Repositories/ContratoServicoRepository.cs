using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
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

        /// <summary>
        /// Cancela UM contrato por expiração e desfaz seus vínculos (proposta recusada,
        /// projeto reaberto), tudo numa transação. O cancelamento é condicional: só
        /// acontece se o contrato AINDA estiver pendente de assinatura e vencido no
        /// momento da escrita. Se outra réplica já o cancelou — ou o usuário assinou /
        /// cancelou entre a leitura e a escrita — nada é alterado e devolve false.
        /// </summary>
        public async Task<bool> CancelarPorExpiracaoAsync(
            Guid contratoId, Guid propostaId, int projetoId, DateTime agora,
            string motivoContrato, string motivoProposta)
        {
            await using var tx = await _ctx.Database.BeginTransactionAsync();

            var cancelados = await _ctx.ContratosServico
                .Where(c => c.Id == contratoId &&
                            (c.Status == ContratoServicoStatus.Gerado ||
                             c.Status == ContratoServicoStatus.AguardandoAssinatura) &&
                            c.ExpiraEm < agora)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, ContratoServicoStatus.Cancelado)
                    .SetProperty(c => c.MotivoCancelamento, motivoContrato)
                    .SetProperty(c => c.CanceladoEm, agora));

            if (cancelados == 0)
            {
                await tx.RollbackAsync();
                return false;
            }

            // Mesmo efeito do cancelamento gratuito (ver ContratoServicoService).
            await _ctx.PropostasProjeto
                .Where(p => p.Id == propostaId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, StatusPropostaProjeto.Recusada)
                    .SetProperty(p => p.MotivoCancelamento, motivoProposta)
                    .SetProperty(p => p.CanceladoEm, agora)
                    .SetProperty(p => p.AtualizadoEm, agora));

            await _ctx.Projetos
                .Where(p => p.Id == projetoId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, StatusProjeto.Aberto)
                    .SetProperty(p => p.FreelancerSelecionadoId, (int?)null)
                    .SetProperty(p => p.AtualizadoEm, agora));

            await tx.CommitAsync();
            return true;
        }

        public async Task AddAsync(ContratoServico contrato)
            => await _ctx.ContratosServico.AddAsync(contrato);

        public async Task AddSnapshotAsync(ContratoSnapshot snapshot)
            => await _ctx.ContratoSnapshots.AddAsync(snapshot);

        public async Task AddHistoricoAsync(HistoricoAssinatura historico)
            => await _ctx.HistoricosAssinatura.AddAsync(historico);

        public async Task SaveChangesAsync()
            => await _ctx.SaveChangesAsync();
    }
}
