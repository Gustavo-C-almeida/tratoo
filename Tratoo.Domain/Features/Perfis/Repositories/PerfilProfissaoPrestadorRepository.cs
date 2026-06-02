using Tratoo.Domain.Data;
using Tratoo.Domain.Models.Prestador;
using Microsoft.EntityFrameworkCore;

namespace Tratoo.Domain.Features.Perfis
{
    public class PerfilProfissaoPrestadorRepository : IPerfilProfissaoPrestadorRepository
    {
        private readonly TratooContext _context;

        public PerfilProfissaoPrestadorRepository(TratooContext context)
        {
            _context = context;
        }

        public async Task<Prestador?> GetCompletoAsync(int id)
        {
            return await _context.Prestadores
                // Competencias + seus vínculos com Experiência, Certificação e Portfólio
                .Include(p => p.Competencias)
                    .ThenInclude(c => c.CompetenciaExperiencias)
                .Include(p => p.Competencias)
                    .ThenInclude(c => c.CompetenciaCertificacoes)
                .Include(p => p.Competencias)
                    .ThenInclude(c => c.CompetenciaPortfolios)

                // Experiências + competências vinculadas (com dados da competência)
                .Include(p => p.Experiencias)
                    .ThenInclude(e => e.CompetenciaExperiencias)
                        .ThenInclude(ce => ce.Competencia)

                // Certificações + competências vinculadas
                .Include(p => p.Certificacoes)
                    .ThenInclude(c => c.CompetenciaCertificacoes)
                        .ThenInclude(cc => cc.Competencia)

                // Portfólio + competências vinculadas
                .Include(p => p.Portfolio)
                    .ThenInclude(pt => pt.CompetenciaPortfolios)
                        .ThenInclude(cp => cp.Competencia)

                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task SaveAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
