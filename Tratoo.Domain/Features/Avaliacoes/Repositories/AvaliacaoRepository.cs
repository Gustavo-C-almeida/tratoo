using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Avaliacoes
{
    public class AvaliacaoRepository : IAvaliacaoRepository
    {
        private readonly TratooContext _db;

        public AvaliacaoRepository(TratooContext db)
        {
            _db = db;
        }

        public async Task<Avaliacao?> GetByIdAsync(Guid id)
        {
            return await _db.Avaliacoes
                .Include(a => a.ContratoServico)
                    .ThenInclude(c => c.Projeto)
                .Include(a => a.Avaliador)
                .Include(a => a.Avaliado)
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task<Avaliacao?> GetPendenteDoUsuarioNoContratoAsync(Guid contratoServicoId, int avaliadorId)
        {
            return await _db.Avaliacoes
                .AsNoTracking()
                .AsSplitQuery()
                .Include(a => a.ContratoServico)
                    .ThenInclude(c => c.Projeto)
                .Include(a => a.Avaliado)
                .FirstOrDefaultAsync(a =>
                    a.ContratoServicoId == contratoServicoId &&
                    a.AvaliadorId == avaliadorId);
        }

        public async Task<List<Avaliacao>> GetDoContratoAsync(Guid contratoServicoId)
        {
            return await _db.Avaliacoes
                .Include(a => a.Avaliador)
                .Include(a => a.Avaliado)
                .Where(a => a.ContratoServicoId == contratoServicoId)
                .ToListAsync();
        }

        public async Task<List<Avaliacao>> GetPublicasPorAvaliadoAsync(int avaliadoId, int pagina, int tamanhoPagina)
        {
            return await _db.Avaliacoes
                .AsNoTracking()
                .AsSplitQuery()
                .Include(a => a.Avaliador)
                .Include(a => a.ContratoServico)
                    .ThenInclude(c => c.Projeto)
                .Where(a =>
                    a.AvaliadoId == avaliadoId &&
                    a.Status == StatusAvaliacao.Publicada &&
                    a.Publica == true &&
                    a.Nota != null)
                .OrderByDescending(a => a.PublicadaEm)
                .Skip((pagina - 1) * tamanhoPagina)
                .Take(tamanhoPagina)
                .ToListAsync();
        }

        public async Task<List<Avaliacao>> GetPendentesPorAvaliadorAsync(int avaliadorId)
        {
            return await _db.Avaliacoes
                .AsNoTracking()
                .AsSplitQuery()
                .Include(a => a.ContratoServico)
                    .ThenInclude(c => c.Projeto)
                .Include(a => a.Avaliado)
                .Where(a =>
                    a.AvaliadorId == avaliadorId &&
                    a.Status == StatusAvaliacao.Pendente &&
                    a.Nota == null)
                .OrderByDescending(a => a.CriadoEm)
                .ToListAsync();
        }

        public async Task<List<Avaliacao>> GetPendentesExpiradosAsync(DateTime limite)
        {
            return await _db.Avaliacoes
                .Where(a =>
                    a.Status == StatusAvaliacao.Pendente &&
                    a.CriadoEm < limite)
                .ToListAsync();
        }

        public async Task<ReputacaoResumo?> GetReputacaoAsync(int userId)
        {
            return await _db.ReputacaoResumos
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UsuarioId == userId);
        }

        public async Task AddAsync(Avaliacao avaliacao)
        {
            await _db.Avaliacoes.AddAsync(avaliacao);
        }

        public async Task UpsertReputacaoAsync(ReputacaoResumo resumo)
        {
            var existente = await _db.ReputacaoResumos
                .FirstOrDefaultAsync(r => r.UsuarioId == resumo.UsuarioId);

            if (existente == null)
            {
                await _db.ReputacaoResumos.AddAsync(resumo);
            }
            else
            {
                existente.MediaGeral = resumo.MediaGeral;
                existente.TotalAvaliacoes = resumo.TotalAvaliacoes;
                existente.Distribuicao1 = resumo.Distribuicao1;
                existente.Distribuicao2 = resumo.Distribuicao2;
                existente.Distribuicao3 = resumo.Distribuicao3;
                existente.Distribuicao4 = resumo.Distribuicao4;
                existente.Distribuicao5 = resumo.Distribuicao5;
                existente.UltimaAtualizacao = resumo.UltimaAtualizacao;
            }
        }

        public async Task<List<Avaliacao>> GetPendentesParaLembreteAsync(DateTime limiteInicio, DateTime limiteFim)
        {
            return await _db.Avaliacoes
                .AsNoTracking()
                .Include(a => a.Avaliador)
                .Include(a => a.ContratoServico)
                    .ThenInclude(c => c.Projeto)
                .Where(a =>
                    a.Status == StatusAvaliacao.Pendente &&
                    a.Nota == null &&
                    a.CriadoEm >= limiteInicio &&
                    a.CriadoEm < limiteFim)
                .ToListAsync();
        }

        public async Task<int> CountAvaliacoesMutuasAsync(int userId1, int userId2)
        {
            return await _db.Avaliacoes
                .CountAsync(a =>
                    (a.AvaliadorId == userId1 && a.AvaliadoId == userId2) ||
                    (a.AvaliadorId == userId2 && a.AvaliadoId == userId1));
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
