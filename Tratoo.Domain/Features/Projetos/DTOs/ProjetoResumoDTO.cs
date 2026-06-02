using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Projetos
{
    public class ProjetoResumoDTO
    {
        public int Id { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string DescricaoResumida { get; set; } = string.Empty;
        public CategoriaProjet Categoria { get; set; }
        public decimal OrcamentoMin { get; set; }
        public decimal OrcamentoMax { get; set; }
        public DateTime PrazoEntrega { get; set; }
        public List<string> Habilidades { get; set; } = new();
        public NivelFreelancerProjet? NivelFreelancer { get; set; }
        public int TotalPropostas { get; set; }
        public DateTime PublicadoEm { get; set; }
        public StatusProjeto Status { get; set; }

        // Contratante resumido
        public int ContratanteId { get; set; }
        public string ContratanteNome { get; set; } = string.Empty;
        public bool ContratanteNovo { get; set; }
    }
}
