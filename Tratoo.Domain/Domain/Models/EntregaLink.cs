namespace Tratoo.Domain.Models
{
    /// <summary>Link externo de evidência anexado a uma Entrega (ex: repositório, drive, deploy).</summary>
    public class EntregaLink
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid EntregaId { get; set; }
        public Entrega Entrega { get; set; } = null!;

        public string Url { get; set; } = string.Empty;
        public string? Descricao { get; set; }

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    }
}
