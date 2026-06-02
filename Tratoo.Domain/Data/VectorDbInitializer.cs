using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tratoo.Domain.Data
{
    /// <summary>
    /// Inicializa o schema de vetores no PostgreSQL (Neon).
    /// Cria extensão, tabelas e índices HNSW na subida da aplicação.
    /// Seguro para executar múltiplas vezes (IF NOT EXISTS em tudo).
    /// </summary>
    public class VectorDbInitializer
    {
        private readonly VectorContext _vectorDb;
        private readonly ILogger<VectorDbInitializer> _logger;

        public VectorDbInitializer(VectorContext vectorDb, ILogger<VectorDbInitializer> logger)
        {
            _vectorDb = vectorDb;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Inicializando schema vetorial no Neon PostgreSQL...");

            // 1. Extensão pgvector (Neon já tem pré-instalada, mas garante)
            await _vectorDb.Database.ExecuteSqlRawAsync(
                "CREATE EXTENSION IF NOT EXISTS vector;");

            // 2. Tabela de embeddings de prestadores (1536 dims — text-embedding-3-small)
            await _vectorDb.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS prestador_embeddings (
                    prestador_id   INTEGER     PRIMARY KEY,
                    embedding      VECTOR(1536) NOT NULL,
                    texto_normalizado TEXT      NOT NULL,
                    modelo_versao  VARCHAR(50) NOT NULL DEFAULT 'text-embedding-3-small',
                    indexado_em    TIMESTAMP   NOT NULL DEFAULT NOW()
                );
            ");

            // 3. Tabela de embeddings de projetos
            await _vectorDb.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS projeto_embeddings (
                    projeto_id     INTEGER     PRIMARY KEY,
                    embedding      VECTOR(1536) NOT NULL,
                    texto_normalizado TEXT      NOT NULL,
                    modelo_versao  VARCHAR(50) NOT NULL DEFAULT 'text-embedding-3-small',
                    indexado_em    TIMESTAMP   NOT NULL DEFAULT NOW()
                );
            ");

            // 4. Índices HNSW — permitem busca por cosseno em milhões de vetores em ms
            await _vectorDb.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS idx_prestador_embedding
                ON prestador_embeddings USING hnsw (embedding vector_cosine_ops);
            ");

            await _vectorDb.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS idx_projeto_embedding
                ON projeto_embeddings USING hnsw (embedding vector_cosine_ops);
            ");

            _logger.LogInformation("Schema vetorial inicializado com sucesso (Neon + pgvector + HNSW).");
        }
    }
}
