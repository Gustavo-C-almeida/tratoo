using Tratoo.Domain.Enums;

namespace Tratoo.API.Requests
{
    public class CriarProjetoRequest
    {
        public string Titulo { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public CategoriaProjet Categoria { get; set; }
        public decimal OrcamentoMin { get; set; }
        public decimal OrcamentoMax { get; set; }
        public DateTime PrazoEntrega { get; set; }
        public List<string>? Habilidades { get; set; }
        public NivelFreelancerProjet? NivelFreelancer { get; set; }
        public VisibilidadeProjeto Visibilidade { get; set; } = VisibilidadeProjeto.Publico;
        public IdiomaProjet Idioma { get; set; } = IdiomaProjet.Portugues;
        public int NumFreelancersDesejados { get; set; } = 1;
        public bool PublicarImediatamente { get; set; } = false;
    }
}
