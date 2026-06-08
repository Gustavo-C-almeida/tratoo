namespace Tratoo.Domain.Features.IA
{
    // ─── Resultados de busca ────────────────────────────────────────────────────

    public class PrestadorBuscaResultDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? TituloProfissional { get; set; }
        public string? FotoUrl { get; set; }
        public double MediaAvaliacoes { get; set; }
        public int TotalAvaliacoes { get; set; }
        public int PorcentagemCompleto { get; set; }
        public int NivelVerificacao { get; set; }
        public int ContratosEncerrados { get; set; }
        /// <summary>Top competências do prestador (até 5).</summary>
        public List<string> Competencias { get; set; } = [];
        /// <summary>Trecho da bio (até 150 chars).</summary>
        public string? Bio { get; set; }
        /// <summary>Cosine similarity 0..1 com a query do usuário.</summary>
        public float Similaridade { get; set; }
        /// <summary>Score ponderado: 42% semântica + 22% habilidades + 12% comprovação (competências com experiência/portfólio/certificação) + 8% avaliação + 9% completude + 4% contratos + 3% verificação.</summary>
        public float ScoreComposto { get; set; }
        /// <summary>Competências do prestador que coincidiram com a busca — base para "Possui X, Y e Z".</summary>
        public List<string> CompetenciasMatchadas { get; set; } = [];
    }

    public class ProjetoBuscaResultDTO
    {
        public int Id { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public decimal OrcamentoMin { get; set; }
        public decimal OrcamentoMax { get; set; }
        public DateTime PrazoEntrega { get; set; }
        public int TotalPropostas { get; set; }
        /// <summary>Habilidades/tags do projeto.</summary>
        public List<string> Habilidades { get; set; } = [];
        public float Similaridade { get; set; }
    }

    // ─── Filtros de busca ───────────────────────────────────────────────────────

    public class FiltrosBuscaPrestadoresDTO
    {
        /// <summary>Query semântica livre. Ex: "dev backend financeiro pagamentos".</summary>
        public string? Q { get; set; }
        public string? Categoria { get; set; }
        /// <summary>Filtra apenas prestadores com NivelVerificacao >= 2.</summary>
        public bool? ApenasVerificados { get; set; }
        public double? AvaliacaoMin { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ─── Admin / Status ─────────────────────────────────────────────────────────

    public class StatusIndexacaoDTO
    {
        public int TotalPrestadoresIndexados { get; set; }
        public int TotalProjetosIndexados { get; set; }
        public int PrestadoresSemEmbedding { get; set; }
        public int ProjetosSemEmbedding { get; set; }
        public DateTime? UltimaIndexacaoPrestador { get; set; }
        public DateTime? UltimaIndexacaoProjeto { get; set; }
    }
}
