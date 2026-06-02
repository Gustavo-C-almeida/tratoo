using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public class MensagemProjetoRepository : IMensagemProjetoRepository
    {
        private readonly TratooContext _ctx;

        public MensagemProjetoRepository(TratooContext ctx) => _ctx = ctx;

        // Melhoria 2 + 4: ORDER BY ASC (mais antigas no topo) e filtrado por par
        public async Task<List<MensagemProjeto>> GetDoPorAsync(
            int projetoId, int prestadorId, int pagina, int tamanhoPagina = 30)
            => await _ctx.MensagensProjeto
                .Include(m => m.Remetente)
                .Where(m => m.ProjetoId == projetoId && m.PrestadorId == prestadorId)
                .OrderBy(m => m.EnviadoEm)
                .Skip((pagina - 1) * tamanhoPagina)
                .Take(tamanhoPagina)
                .ToListAsync();

        public async Task<int> ContarPorAsync(int projetoId, int prestadorId)
            => await _ctx.MensagensProjeto
                .CountAsync(m => m.ProjetoId == projetoId && m.PrestadorId == prestadorId);

        // Para a lista de chats do prestador: última mensagem de cada conversa
        public async Task<List<MensagemProjeto>> GetUltimasMensagensPorPrestadorAsync(int prestadorId)
            => await _ctx.MensagensProjeto
                .Include(m => m.Projeto)
                .Include(m => m.Remetente)
                .Where(m => m.PrestadorId == prestadorId)
                .GroupBy(m => m.ProjetoId)
                .Select(g => g.OrderByDescending(m => m.EnviadoEm).First())
                .ToListAsync();

        // Para a lista de chats do contratante: última mensagem de cada par
        public async Task<List<MensagemProjeto>> GetUltimasMensagensPorContratanteAsync(int contratanteId)
            => await _ctx.MensagensProjeto
                .Include(m => m.Projeto)
                .Include(m => m.Remetente)
                .Where(m => m.Projeto.ContratanteId == contratanteId && m.PrestadorId != null)
                .GroupBy(m => new { m.ProjetoId, m.PrestadorId })
                .Select(g => g.OrderByDescending(m => m.EnviadoEm).First())
                .ToListAsync();

        public async Task AddAsync(MensagemProjeto mensagem)
            => await _ctx.MensagensProjeto.AddAsync(mensagem);

        public async Task SaveChangesAsync()
            => await _ctx.SaveChangesAsync();
    }
}
