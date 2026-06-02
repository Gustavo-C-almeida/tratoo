using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    public class Projeto
    {
        public int Id { get; set; }

        public int ContratanteId { get; set; }
        public Contratante Contratante { get; set; } = null!;

        public string Titulo { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;

        public CategoriaProjet Categoria { get; set; }

        public decimal OrcamentoMin { get; set; }
        public decimal OrcamentoMax { get; set; }

        public DateTime PrazoEntrega { get; set; }

        // Campos opcionais
        /// <summary>JSON array de strings, máx. 10 tags.</summary>
        public string? Habilidades { get; set; }

        public NivelFreelancerProjet? NivelFreelancer { get; set; }
        public VisibilidadeProjeto Visibilidade { get; set; } = VisibilidadeProjeto.Publico;
        public IdiomaProjet Idioma { get; set; } = IdiomaProjet.Portugues;
        public int NumFreelancersDesejados { get; set; } = 1;

        // Freelancer selecionado após aceite de proposta
        public int? FreelancerSelecionadoId { get; set; }
        public Prestador.Prestador? FreelancerSelecionado { get; set; }

        public StatusProjeto Status { get; set; } = StatusProjeto.Rascunho;
        public bool Publicado { get; set; } = false;
        public DateTime? PublicadoEm { get; set; }

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
        public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
        public DateTime? CanceladoEm { get; set; }
        public string? MotivoCancelamento { get; set; }

        /// <summary>Contador de propostas recebidas.</summary>
        public int TotalPropostas { get; set; } = 0;

        public ICollection<PropostaProjeto> Propostas { get; set; } = new List<PropostaProjeto>();
    }
}
