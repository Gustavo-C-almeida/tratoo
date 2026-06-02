using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public class ConviteProjetoRepository : IConviteProjetoRepository
    {
        private readonly TratooContext _context;

        public ConviteProjetoRepository(TratooContext context) => _context = context;

        public async Task<ConviteProjeto?> GetByIdAsync(Guid id)
            => await _context.ConvitesProjeto
                .Include(c => c.Projeto)
                    .ThenInclude(p => p.Contratante)
                .Include(c => c.Contratante)
                .Include(c => c.Prestador)
                .FirstOrDefaultAsync(c => c.Id == id);

        public async Task<ConviteProjeto?> GetAtivoByPrestadorEProjetoAsync(int prestadorId, int projetoId)
            => await _context.ConvitesProjeto
                .FirstOrDefaultAsync(c =>
                    c.PrestadorId == prestadorId &&
                    c.ProjetoId == projetoId &&
                    c.Status == StatusConvite.Pendente);

        public async Task<List<ConviteProjeto>> GetDoPrestadorAsync(int prestadorId)
            => await _context.ConvitesProjeto
                .Include(c => c.Projeto)
                .Include(c => c.Contratante)
                .Where(c => c.PrestadorId == prestadorId)
                .OrderByDescending(c => c.CriadoEm)
                .ToListAsync();

        public async Task<List<ConviteProjeto>> GetDoProjetoAsync(int projetoId)
            => await _context.ConvitesProjeto
                .Include(c => c.Prestador)
                .Where(c => c.ProjetoId == projetoId)
                .OrderByDescending(c => c.CriadoEm)
                .ToListAsync();

        public async Task AddAsync(ConviteProjeto convite)
            => await _context.ConvitesProjeto.AddAsync(convite);

        public async Task SaveChangesAsync()
            => await _context.SaveChangesAsync();
    }
}
