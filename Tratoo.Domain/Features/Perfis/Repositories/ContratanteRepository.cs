using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Perfis
{
    public class ContratanteRepository : IContratanteRepository
    {
        private readonly TratooContext _context;

        public ContratanteRepository(TratooContext context)
        {
            _context = context;
        }

        // Buscar simples
        public async Task<Contratante?> GetByIdAsync(int id)
        {
            return await _context.Contratantes
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        // Buscar completo (com relacionamentos)
        public async Task<Contratante?> GetCompletoAsync(int id)
        {
            return await _context.Contratantes
                .Include(c => c.Contratos)
                .Include(c => c.PropostasEnviadas)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task UpdateAsync(Contratante contratante)
        {
            _context.Contratantes.Update(contratante);
            await Task.CompletedTask;
        }

        public async Task SaveAsync()
        {
            await _context.SaveChangesAsync();
        }

        public async Task<(int Total, int Concluidos, decimal? ValorMedio)> GetMetricasProjetosAsync(int contratanteId)
        {
            var projetos = await _context.Projetos
                .Where(p => p.ContratanteId == contratanteId && p.Publicado)
                .Select(p => new { p.Status, p.OrcamentoMin, p.OrcamentoMax })
                .ToListAsync();

            if (projetos.Count == 0)
                return (0, 0, null);

            var total = projetos.Count;
            var concluidos = projetos.Count(p => p.Status == StatusProjeto.Concluido);
            var valorMedio = (decimal)projetos.Average(p => (double)(p.OrcamentoMin + p.OrcamentoMax) / 2.0);

            return (total, concluidos, valorMedio);
        }

        public async Task<List<Projeto>> GetUltimosProjetosAsync(int contratanteId, int quantidade = 5)
        {
            return await _context.Projetos
                .AsNoTracking()
                .Where(p => p.ContratanteId == contratanteId && p.Publicado)
                .OrderByDescending(p => p.PublicadoEm ?? p.CriadoEm)
                .Take(quantidade)
                .ToListAsync();
        }
    }

}
