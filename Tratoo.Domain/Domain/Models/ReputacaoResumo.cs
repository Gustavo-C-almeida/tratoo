namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Cache pré-calculado da reputação pública de um usuário.
    /// Atualizado sempre que uma avaliação é publicada, ocultada ou restaurada.
    /// Evita cálculos pesados em tempo real no carregamento do perfil.
    /// Considera APENAS avaliações: Status=Publicada, Publica=true, e avaliado com AvaliacoesPrivado=false.
    /// </summary>
    public class ReputacaoResumo
    {
        /// <summary>PK e FK para Usuario (1:1).</summary>
        public int UsuarioId { get; set; }

        /// <summary>Média das notas (0 quando não há avaliações públicas).</summary>
        public double MediaGeral { get; set; }

        /// <summary>Total de avaliações que compõem a média pública.</summary>
        public int TotalAvaliacoes { get; set; }

        public int Distribuicao1 { get; set; }
        public int Distribuicao2 { get; set; }
        public int Distribuicao3 { get; set; }
        public int Distribuicao4 { get; set; }
        public int Distribuicao5 { get; set; }

        public DateTime UltimaAtualizacao { get; set; }

        // Navegação
        public Usuario Usuario { get; set; } = null!;
    }
}
