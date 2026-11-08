using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Caminho vetorial da busca semântica contra Postgres + pgvector REAIS: o schema do
    /// <see cref="VectorDbInitializer"/>, a gravação e a leitura do tipo <c>vector</c> e a
    /// tradução de <c>CosineDistance</c> pelo plugin <c>Pgvector.EntityFrameworkCore</c>, na mesma
    /// forma da consulta do <c>BuscaSemanticaService</c>. O plugin depende de APIs internas do
    /// Npgsql EF, então é o primeiro a quebrar numa troca de major do EF Core ou do Npgsql — e
    /// nada além deste teste exercita essa tradução.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class VetoresTests
    {
        private const int Dimensoes = 1536; // VECTOR(1536) do VectorDbInitializer

        private readonly PostgresFixture _pg;

        public VetoresTests(PostgresFixture pg) => _pg = pg;

        private static VectorContext Contexto(string conexao) =>
            new(new DbContextOptionsBuilder<VectorContext>()
                .UseNpgsql(conexao, o => o.UseVector())
                .Options);

        /// <summary>Vetor unitário no eixo <paramref name="eixo"/>, com um pouco do eixo 0.</summary>
        private static Vector Direcao(int eixo, float mistura = 0f)
        {
            var v = new float[Dimensoes];
            v[eixo] = 1f;
            v[0] += mistura;
            return new Vector(v);
        }

        private async Task<string> BancoComSchemaVetorialAsync()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();

            // A extensão existe ANTES da primeira conexão do EF, como em produção: no Neon ela vem
            // instalada e, no compose, o `--migrate-only` a cria em outro processo antes da API.
            // O data source interno do EF lê o catálogo de tipos uma vez, na primeira conexão; se
            // ela acontecesse antes do CREATE EXTENSION, gravar um Vector falharia com
            // "Cannot resolve 'vector'" até o processo reiniciar.
            await using (var c = new NpgsqlConnection(conexao))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", c);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var db = Contexto(conexao))
                await new VectorDbInitializer(db, NullLogger<VectorDbInitializer>.Instance).InitializeAsync();
            return conexao;
        }

        [FactComDocker]
        public async Task Inicializador_CriaExtensaoTabelasEIndicesHnsw_EEIdempotente()
        {
            var conexao = await BancoComSchemaVetorialAsync();

            // Segunda subida (outra réplica, outro deploy): CREATE ... IF NOT EXISTS não pode falhar.
            await using (var db = Contexto(conexao))
                await new VectorDbInitializer(db, NullLogger<VectorDbInitializer>.Instance).InitializeAsync();

            await using var c = new NpgsqlConnection(conexao);
            await c.OpenAsync();

            await using (var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_extension WHERE extname = 'vector'", c))
                Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);

            await using (var cmd = new NpgsqlCommand(
                "SELECT indexname FROM pg_indexes WHERE indexdef LIKE '%USING hnsw%' ORDER BY indexname", c))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                var indices = new List<string>();
                while (await r.ReadAsync()) indices.Add(r.GetString(0));
                Assert.Equal(new[] { "idx_prestador_embedding", "idx_projeto_embedding" }, indices);
            }
        }

        [FactComDocker]
        public async Task Embedding_GravaELeVetorSemPerda()
        {
            var conexao = await BancoComSchemaVetorialAsync();
            var original = Direcao(7, mistura: 0.25f);

            await using (var db = Contexto(conexao))
            {
                db.PrestadorEmbeddings.Add(new PrestadorEmbedding
                {
                    PrestadorId = 1,
                    Embedding = original,
                    TextoNormalizado = "eletricista residencial"
                });
                db.ProjetoEmbeddings.Add(new ProjetoEmbedding
                {
                    ProjetoId = 1,
                    Embedding = original,
                    TextoNormalizado = "instalação elétrica"
                });
                await db.SaveChangesAsync();
            }

            await using (var db = Contexto(conexao))
            {
                var prestador = await db.PrestadorEmbeddings.AsNoTracking().SingleAsync();
                var projeto = await db.ProjetoEmbeddings.AsNoTracking().SingleAsync();

                Assert.Equal(original.ToArray(), prestador.Embedding.ToArray());
                Assert.Equal(original.ToArray(), projeto.Embedding.ToArray());
                Assert.Equal("eletricista residencial", prestador.TextoNormalizado);
            }
        }

        [FactComDocker]
        public async Task CosineDistance_OrdenaPorSimilaridade_ComoNaBuscaSemantica()
        {
            var conexao = await BancoComSchemaVetorialAsync();

            await using (var db = Contexto(conexao))
            {
                // 10 = a própria consulta; 20 = parecido; 30 = ortogonal (sem nada em comum).
                db.PrestadorEmbeddings.AddRange(
                    new PrestadorEmbedding { PrestadorId = 30, Embedding = Direcao(5), TextoNormalizado = "c" },
                    new PrestadorEmbedding { PrestadorId = 10, Embedding = Direcao(1), TextoNormalizado = "a" },
                    new PrestadorEmbedding { PrestadorId = 20, Embedding = Direcao(1, mistura: 0.5f), TextoNormalizado = "b" });
                await db.SaveChangesAsync();
            }

            var qv = Direcao(1);
            await using (var db = Contexto(conexao))
            {
                // Mesma forma da consulta do BuscaSemanticaService: ORDER BY + SELECT da distância.
                var top = await db.PrestadorEmbeddings
                    .OrderBy(e => e.Embedding.CosineDistance(qv))
                    .Take(100)
                    .Select(e => new { e.PrestadorId, Distance = e.Embedding.CosineDistance(qv) })
                    .ToListAsync();

                Assert.Equal(new[] { 10, 20, 30 }, top.Select(t => t.PrestadorId));
                Assert.Equal(0d, top[0].Distance, precision: 6);                  // idêntico
                Assert.Equal(1d - 1d / Math.Sqrt(1.25), top[1].Distance, precision: 5); // cos = 1/√1,25
                Assert.Equal(1d, top[2].Distance, precision: 6);                  // ortogonal

                // A tradução tem que acontecer no banco (operador <=> do pgvector), não no cliente.
                var sql = db.PrestadorEmbeddings
                    .OrderBy(e => e.Embedding.CosineDistance(qv))
                    .Select(e => e.PrestadorId)
                    .ToQueryString();
                Assert.Contains("<=>", sql);
            }
        }
    }
}
