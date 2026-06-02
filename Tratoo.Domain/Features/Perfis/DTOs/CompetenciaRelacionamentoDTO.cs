namespace Tratoo.Domain.Features.Perfis
{
    public class CompetenciaRelacionamentoDTO
    {
        public List<ExperienciaResumoDTO>  Experiencias  { get; set; } = new();
        public List<CertificacaoResumoDTO> Certificacoes { get; set; } = new();
        public List<PortfolioResumoDTO>    Portfolio     { get; set; } = new();
    }

    public class ExperienciaResumoDTO
    {
        public int    Id      { get; set; }
        public string Empresa { get; set; } = string.Empty;
        public string Cargo   { get; set; } = string.Empty;
    }

    public class CertificacaoResumoDTO
    {
        public int    Id   { get; set; }
        public string Nome { get; set; } = string.Empty;
    }

    public class PortfolioResumoDTO
    {
        public int    Id     { get; set; }
        public string Titulo { get; set; } = string.Empty;
    }
}
