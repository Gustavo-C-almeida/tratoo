using Pgvector;

namespace Tratoo.Domain.Models
{
    public class PrestadorEmbedding
    {
        public int PrestadorId { get; set; }
        public Vector Embedding { get; set; } = null!;
        public string TextoNormalizado { get; set; } = string.Empty;
        public string ModeloVersao { get; set; } = "text-embedding-3-small";
        public DateTime IndexadoEm { get; set; } = DateTime.UtcNow;
    }
}
