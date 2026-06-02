using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.IA
{
    public class ProjetoIndexadorService
    {
        private readonly TratooContext _db;
        private readonly VectorContext _vectorDb;
        private readonly IEmbeddingService _ai;
        private readonly TextoNormalizadorService _normalizer;
        private readonly ILogger<ProjetoIndexadorService> _logger;

        public ProjetoIndexadorService(
            TratooContext db,
            VectorContext vectorDb,
            IEmbeddingService ai,
            TextoNormalizadorService normalizer,
            ILogger<ProjetoIndexadorService> logger)
        {
            _db = db;
            _vectorDb = vectorDb;
            _ai = ai;
            _normalizer = normalizer;
            _logger = logger;
        }

        public async Task IndexarAsync(int projetoId)
        {
            try
            {
                var projeto = await _db.Projetos.FindAsync(projetoId);

                if (projeto == null)
                {
                    _logger.LogWarning("Indexação ignorada — projeto {Id} não encontrado.", projetoId);
                    return;
                }

                if (!projeto.Publicado)
                {
                    _logger.LogInformation("Projeto {Id} ainda não publicado — indexação ignorada.", projetoId);
                    return;
                }

                var texto = _normalizer.NormalizarProjeto(projeto);
                var embeddingArr = await _ai.GerarEmbeddingAsync(texto);
                var embedding = new Vector(embeddingArr);

                var existing = await _vectorDb.ProjetoEmbeddings.FindAsync(projetoId);
                if (existing == null)
                {
                    _vectorDb.ProjetoEmbeddings.Add(new ProjetoEmbedding
                    {
                        ProjetoId        = projetoId,
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
                _logger.LogInformation("Projeto {Id} indexado com sucesso.", projetoId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao indexar projeto {Id}.", projetoId);
            }
        }
    }
}
