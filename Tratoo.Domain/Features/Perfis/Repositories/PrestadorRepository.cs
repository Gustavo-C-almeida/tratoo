using Tratoo.Domain.Data;
using Tratoo.Domain.Models.Prestador;
using Microsoft.EntityFrameworkCore;

namespace Tratoo.Domain.Features.Perfis
{
    public class PrestadorRepository : IPrestadorRepository
    {
        private readonly TratooContext _context;

        public PrestadorRepository(TratooContext context)
        {
            _context = context;
        }

        public async Task<Prestador?> GetByIdAsync(int id)
        {
            return await _context.Prestadores
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<Prestador?> GetCompletoAsync(int id)
        {
            return await _context.Prestadores
                .Include(p => p.Competencias)

                // Certificações + competências vinculadas
                .Include(p => p.Certificacoes)
                    .ThenInclude(c => c.CompetenciaCertificacoes)
                        .ThenInclude(cc => cc.Competencia)

                // Experiências + competências vinculadas
                .Include(p => p.Experiencias)
                    .ThenInclude(e => e.CompetenciaExperiencias)
                        .ThenInclude(ce => ce.Competencia)

                // Portfólio + competências vinculadas
                .Include(p => p.Portfolio)
                    .ThenInclude(pt => pt.CompetenciaPortfolios)
                        .ThenInclude(cp => cp.Competencia)

                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task UpdateAsync(Prestador prestador)
        {
            _context.Prestadores.Update(prestador);
            await _context.SaveChangesAsync();
        }

        public async Task SaveAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
