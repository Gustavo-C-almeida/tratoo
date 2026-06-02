using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Projetos
{
    public class ProjetoDetalheDTO
    {
        public int Id { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public CategoriaProjet Categoria { get; set; }
        public decimal OrcamentoMin { get; set; }
        public decimal OrcamentoMax { get; set; }
        public DateTime PrazoEntrega { get; set; }
        public List<string> Habilidades { get; set; } = new();
        public NivelFreelancerProjet? NivelFreelancer { get; set; }
        public VisibilidadeProjeto Visibilidade { get; set; }
        public IdiomaProjet Idioma { get; set; }
        public int NumFreelancersDesejados { get; set; }
        public int TotalPropostas { get; set; }
        public DateTime? PublicadoEm { get; set; }
        public StatusProjeto Status { get; set; }

        // Contratante
        public int ContratanteId { get; set; }
        public string ContratanteNome { get; set; } = string.Empty;
        public string? ContratanteLogoUrl { get; set; }
        public bool ContratanteNovo { get; set; }
    }
}
