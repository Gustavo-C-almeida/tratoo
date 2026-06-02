namespace Tratoo.Domain.Models.Prestador
{
    public class Competencia
    {
        public int Id { get; set; }
        public int PrestadorId { get; set; }
        public string Nome { get; set; }
        public int Nivel { get; set; }

        public Prestador Prestador { get; set; }
        public ICollection<CompetenciaCertificacao> CompetenciaCertificacoes { get; set; }
        public ICollection<CompetenciaExperiencia>  CompetenciaExperiencias  { get; set; }
        public ICollection<CompetenciaPortfolio>    CompetenciaPortfolios    { get; set; }
    }
}
