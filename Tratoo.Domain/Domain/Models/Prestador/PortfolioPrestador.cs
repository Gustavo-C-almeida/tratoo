using System.ComponentModel.DataAnnotations;

namespace Tratoo.Domain.Models.Prestador
{
    public class PortfolioPrestador
    {
        public int Id { get; set; }

        public int PrestadorId { get; set; }

        [Required, MaxLength(120)]
        public string Titulo { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Descricao { get; set; }

        public string? LinkExterno { get; set; }

        /// <summary>URL do arquivo PDF armazenado no Cloudflare R2.</summary>
        public string? ArquivoUrl { get; set; }

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

        public Prestador Prestador { get; set; } = null!;
        public ICollection<CompetenciaPortfolio> CompetenciaPortfolios { get; set; } = new List<CompetenciaPortfolio>();
    }
}
