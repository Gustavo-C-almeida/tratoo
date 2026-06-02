using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Data
{
    /// <summary>
    /// DbContext para embeddings vetoriais no PostgreSQL (Neon).
    /// Schema é criado automaticamente por VectorDbInitializer na subida.
    /// Este contexto apenas mapeia nomes de coluna para corresponder ao schema.
    /// </summary>
    public class VectorContext : DbContext
    {
        public DbSet<PrestadorEmbedding> PrestadorEmbeddings { get; set; }
        public DbSet<ProjetoEmbedding> ProjetoEmbeddings { get; set; }

        public VectorContext(DbContextOptions<VectorContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // PrestadorEmbedding — mapeia nomes de colunas para snake_case
            modelBuilder.Entity<PrestadorEmbedding>(entity =>
            {
                entity.ToTable("prestador_embeddings");
                entity.HasKey(e => e.PrestadorId);

                entity.Property(e => e.PrestadorId)
                    .HasColumnName("prestador_id");
                entity.Property(e => e.Embedding)
                    .HasColumnName("embedding");
                entity.Property(e => e.TextoNormalizado)
                    .HasColumnName("texto_normalizado");
                entity.Property(e => e.ModeloVersao)
                    .HasColumnName("modelo_versao");
                entity.Property(e => e.IndexadoEm)
                    .HasColumnName("indexado_em");
            });

            // ProjetoEmbedding — mapeia nomes de colunas para snake_case
            modelBuilder.Entity<ProjetoEmbedding>(entity =>
            {
                entity.ToTable("projeto_embeddings");
                entity.HasKey(e => e.ProjetoId);

                entity.Property(e => e.ProjetoId)
                    .HasColumnName("projeto_id");
                entity.Property(e => e.Embedding)
                    .HasColumnName("embedding");
                entity.Property(e => e.TextoNormalizado)
                    .HasColumnName("texto_normalizado");
                entity.Property(e => e.ModeloVersao)
                    .HasColumnName("modelo_versao");
                entity.Property(e => e.IndexadoEm)
                    .HasColumnName("indexado_em");
            });
        }
    }
}
