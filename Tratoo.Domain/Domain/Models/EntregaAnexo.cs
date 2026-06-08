namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Arquivo de evidência anexado a uma Entrega. O conteúdo fica no bucket PRIVADO
    /// do Cloudflare R2 — armazenamos apenas a chave do objeto (nunca uma URL pública).
    /// O acesso é feito por URL pré-assinada temporária. Usa soft delete (ExcluidoEm).
    /// </summary>
    public class EntregaAnexo
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid EntregaId { get; set; }
        public Entrega Entrega { get; set; } = null!;

        public string NomeArquivo { get; set; } = string.Empty;

        /// <summary>Chave do objeto no bucket privado do R2 (ex: entregas/{contratoId}/{guid}.pdf).</summary>
        public string ChaveR2 { get; set; } = string.Empty;

        /// <summary>Extensão/tipo do arquivo (ex: ".pdf", ".png").</summary>
        public string TipoArquivo { get; set; } = string.Empty;

        /// <summary>Tamanho em bytes.</summary>
        public long TamanhoArquivo { get; set; }

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

        // ── Soft delete ───────────────────────────────────────────────────────────
        public DateTime? ExcluidoEm { get; set; }
    }
}
