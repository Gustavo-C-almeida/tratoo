using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Projetos
{
    public class ProjetoRepository : IProjetoRepository
    {
        private readonly TratooContext _context;

        public ProjetoRepository(TratooContext context) => _context = context;

        public async Task<Projeto?> GetByIdAsync(int id)
            => await _context.Projetos
                .Include(p => p.Contratante)
                .FirstOrDefaultAsync(p => p.Id == id);

        public async Task<(List<Projeto> Itens, int Total)> BuscarAsync(FiltrosProjetoDTO filtros)
        {
            var query = _context.Projetos
                .AsNoTracking()
                .Include(p => p.Contratante)
                .Where(p => p.Status == StatusProjeto.Aberto
                         && p.Visibilidade == VisibilidadeProjeto.Publico)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtros.Q))
            {
                var q = filtros.Q.ToLower();
                query = query.Where(p =>
                    p.Titulo.ToLower().Contains(q) ||
                    p.Descricao.ToLower().Contains(q));
            }

            if (filtros.Categoria.HasValue)
                query = query.Where(p => p.Categoria == filtros.Categoria.Value);

            if (filtros.NivelFreelancer.HasValue)
                query = query.Where(p => p.NivelFreelancer == filtros.NivelFreelancer.Value);

            // OrcamentoMin do filtro: projetos cujo OrcamentoMax >= valor informado
            if (filtros.OrcamentoMin.HasValue)
                query = query.Where(p => p.OrcamentoMax >= filtros.OrcamentoMin.Value);

            // OrcamentoMax do filtro: projetos cujo OrcamentoMin <= valor informado
            if (filtros.OrcamentoMax.HasValue)
                query = query.Where(p => p.OrcamentoMin <= filtros.OrcamentoMax.Value);

            if (filtros.PrazoAte.HasValue)
                query = query.Where(p => p.PrazoEntrega >= filtros.PrazoAte.Value);

            // Filtro por habilidades: projeto contém todas as habilidades informadas
            if (filtros.Habilidades != null && filtros.Habilidades.Any())
            {
                foreach (var h in filtros.Habilidades)
                {
                    var tag = h.ToLower();
                    query = query.Where(p => p.Habilidades != null && p.Habilidades.ToLower().Contains(tag));
                }
            }

            var total = await query.CountAsync();

            query = filtros.OrderBy switch
            {
                "menorOrcamento"  => query.OrderBy(p => p.OrcamentoMin),
                "maiorOrcamento"  => query.OrderByDescending(p => p.OrcamentoMax),
                "relevancia"      => query.OrderByDescending(p => p.TotalPropostas),
                _                 => query.OrderByDescending(p => p.PublicadoEm)
            };

            var pagina = Math.Max(1, filtros.Page);
            var itens = await query
                .Skip((pagina - 1) * 20)
                .Take(20)
                .ToListAsync();

            return (itens, total);
        }

        public async Task<List<Projeto>> GetDoContratanteAsync(int contratanteId)
            => await _context.Projetos
                .Where(p => p.ContratanteId == contratanteId)
                .OrderByDescending(p => p.CriadoEm)
                .ToListAsync();

        public async Task AddAsync(Projeto projeto)
            => await _context.Projetos.AddAsync(projeto);

        public async Task SaveChangesAsync()
            => await _context.SaveChangesAsync();
    }
}
