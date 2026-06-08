using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.IA
{
    public class PrestadorIndexadorService
    {
        private readonly TratooContext _db;
        private readonly VectorContext _vectorDb;
        private readonly IEmbeddingService _ai;
        private readonly TextoNormalizadorService _normalizer;
        private readonly ILogger<PrestadorIndexadorService> _logger;

        public PrestadorIndexadorService(
            TratooContext db,
            VectorContext vectorDb,
            IEmbeddingService ai,
            TextoNormalizadorService normalizer,
            ILogger<PrestadorIndexadorService> logger)
        {
            _db = db;
            _vectorDb = vectorDb;
            _ai = ai;
            _normalizer = normalizer;
            _logger = logger;
        }

        /// <summary>
        /// Remove o embedding do prestador do índice vetorial. Usado quando a conta é
        /// excluída (LGPD) — garante que prestadores deletados não apareçam na busca.
        /// </summary>
        public async Task RemoverAsync(int prestadorId)
        {
            try
            {
                var existing = await _vectorDb.PrestadorEmbeddings.FindAsync(prestadorId);
                if (existing == null) return;

                _vectorDb.PrestadorEmbeddings.Remove(existing);
                await _vectorDb.SaveChangesAsync();
                _logger.LogInformation("Embedding do prestador {Id} removido do índice (conta excluída).", prestadorId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao remover embedding do prestador {Id}.", prestadorId);
            }
        }

        public async Task IndexarAsync(int prestadorId)
        {
            try
            {
                var prestador = await _db.Prestadores
                    .Include(p => p.Competencias)
                    .Include(p => p.Experiencias)
                    .Include(p => p.Certificacoes)
                    .Include(p => p.Portfolio)
                    .FirstOrDefaultAsync(p => p.Id == prestadorId);

                if (prestador == null)
                {
                    _logger.LogWarning("Indexação ignorada — prestador {Id} não encontrado.", prestadorId);
                    return;
                }

                List<string>? comentariosPublicos = null;
                if (!prestador.AvaliacoesPrivado)
                {
                    comentariosPublicos = await _db.Avaliacoes
                        .Where(a => a.AvaliadoId == prestadorId
                               && a.Status == StatusAvaliacao.Publicada
                               && a.Publica
                               && a.Comentario != null)
                        .Select(a => a.Comentario!)
                        .ToListAsync();
                }

                var texto = _normalizer.NormalizarPrestador(prestador, comentariosPublicos);

                if (string.IsNullOrWhiteSpace(texto) || texto.Length < 20)
                {
                    _logger.LogInformation(
                        "Prestador {Id}: perfil sem dados suficientes para indexação (texto < 20 chars).",
                        prestadorId);
                    return;
                }

                var embeddingArr = await _ai.GerarEmbeddingAsync(texto);
                var embedding = new Vector(embeddingArr);

                var existing = await _vectorDb.PrestadorEmbeddings.FindAsync(prestadorId);
                if (existing == null)
                {
                    _vectorDb.PrestadorEmbeddings.Add(new PrestadorEmbedding
                    {
                        PrestadorId      = prestadorId,
                        Embedding        = embedding,
                        TextoNormalizado = texto,
                        IndexadoEm       = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Embedding        = embedding;
                    existing.TextoNormalizado = texto;
                    existing.IndexadoEm       = DateTime.UtcNow;
                }

                await _vectorDb.SaveChangesAsync();
                _logger.LogInformation("Prestador {Id} indexado com sucesso ({Chars} chars).",
                    prestadorId, texto.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao indexar prestador {Id}.", prestadorId);
            }
        }
    }
}
