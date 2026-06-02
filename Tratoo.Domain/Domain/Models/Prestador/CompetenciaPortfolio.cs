namespace Tratoo.Domain.Models.Prestador
{
    public class CompetenciaPortfolio
    {
        public int CompetenciaId { get; set; }
        public Competencia Competencia { get; set; } = null!;

        public int PortfolioPrestadorId { get; set; }
        public PortfolioPrestador PortfolioPrestador { get; set; } = null!;
    }
}
