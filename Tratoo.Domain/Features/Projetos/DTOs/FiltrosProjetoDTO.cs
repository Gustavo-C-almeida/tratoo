using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Projetos
{
    public class FiltrosProjetoDTO
    {
        public string? Q { get; set; }
        public CategoriaProjet? Categoria { get; set; }
        public decimal? OrcamentoMin { get; set; }
        public decimal? OrcamentoMax { get; set; }
        public NivelFreelancerProjet? NivelFreelancer { get; set; }
        public List<string>? Habilidades { get; set; }
        public DateTime? PrazoAte { get; set; }
        public string OrderBy { get; set; } = "recentes";
        public int Page { get; set; } = 1;
    }
}
