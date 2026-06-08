using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Propostas
{
    public class PropostaProjetoRepository : IPropostaProjetoRepository
    {
        private readonly TratooContext _ctx;

        public PropostaProjetoRepository(TratooContext ctx) => _ctx = ctx;

        public async Task<PropostaProjeto?> GetByIdAsync(Guid id)
            => await _ctx.PropostasProjeto
                .Include(p => p.Prestador)
                .Include(p => p.Projeto)
                    .ThenInclude(pr => pr.Contratante)
                .FirstOrDefaultAsync(p => p.Id == id);

        public async Task<PropostaProjeto?> GetByIdComVersoesAsync(Guid id)
            => await _ctx.PropostasProjeto
                .Include(p => p.Prestador)
                .Include(p => p.Projeto)
                    .ThenInclude(pr => pr.Contratante)
                .Include(p => p.Versoes.OrderBy(v => v.Versao))
                .FirstOrDefaultAsync(p => p.Id == id);

        public async Task<PropostaProjeto?> GetAtivaByPrestadorEProjetoAsync(int prestadorId, int projetoId)
            => await _ctx.PropostasProjeto
                .FirstOrDefaultAsync(p =>
                    p.PrestadorId == prestadorId &&
                    p.ProjetoId == projetoId &&
                    p.Status != StatusPropostaProjeto.Recusada &&
                    p.Status != StatusPropostaProjeto.Expirada &&
                    p.Status != StatusPropostaProjeto.Convertida);

        public async Task<PropostaProjeto?> GetAtivaByConviteIdAsync(Guid conviteId)
            => await _ctx.PropostasProjeto
                .FirstOrDefaultAsync(p =>
                    p.ConviteId == conviteId &&
                    p.Status != StatusPropostaProjeto.Recusada &&
                    p.Status != StatusPropostaProjeto.Expirada &&
                    p.Status != StatusPropostaProjeto.Convertida);

        public async Task<List<PropostaProjeto>> GetDoProjetoAsync(int projetoId)
            => await _ctx.PropostasProjeto
                .Include(p => p.Prestador)
                .Include(p => p.Projeto)
                .Include(p => p.Versoes.OrderByDescending(v => v.Versao).Take(1))
                .Where(p => p.ProjetoId == projetoId)
                .OrderByDescending(p => p.CriadoEm)
                .ToListAsync();

        public async Task<List<PropostaProjeto>> GetDoPrestadorAsync(int prestadorId)
            => await _ctx.PropostasProjeto
                .Include(p => p.Projeto)
                .Include(p => p.Versoes.OrderByDescending(v => v.Versao).Take(1))
                .Where(p => p.PrestadorId == prestadorId)
                .OrderByDescending(p => p.AtualizadoEm)
                .ToListAsync();

        public async Task<List<PropostaProjeto>> GetExpiradas(DateTime agora)
            => await _ctx.PropostasProjeto
                .Where(p =>
                    (p.Status == StatusPropostaProjeto.Submitted ||
                     p.Status == StatusPropostaProjeto.EmNegociacao) &&
                    p.ValidoAte < agora)
                .ToListAsync();

        public async Task AddAsync(PropostaProjeto proposta)
            => await _ctx.PropostasProjeto.AddAsync(proposta);

        public async Task AddVersaoAsync(PropostaVersao versao)
            => await _ctx.PropostaVersoes.AddAsync(versao);

        public async Task<PropostaVersao?> GetVersaoAsync(Guid propostaId, int numeroVersao)
            => await _ctx.PropostaVersoes
                .FirstOrDefaultAsync(v => v.PropostaId == propostaId && v.Versao == numeroVersao);

        public async Task SaveChangesAsync()
            => await _ctx.SaveChangesAsync();
    }
}
