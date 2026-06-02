namespace Tratoo.Domain.Features.Perfis
{
    public class PortfolioDTO
    {
        public int Id { get; set; }
        public int PrestadorId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string? LinkExterno { get; set; }
        public string? ArquivoUrl { get; set; }
        public DateTime CriadoEm { get; set; }

        public List<CompetenciaDTO> Competencias { get; set; } = new();
    }
}
