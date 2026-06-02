using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Avaliação bilateral entre contratante e prestador após conclusão de contrato.
    /// Implementa blind review: ambas as avaliações ficam Pendente até que os dois lados
    /// submetam, evitando retaliação e revenge review.
    /// </summary>
    public class Avaliacao
    {
        public Guid Id { get; set; }

        /// <summary>Contrato que originou a avaliação. Garante que só contratos concluídos geram reputação.</summary>
        public Guid ContratoServicoId { get; set; }

        /// <summary>ID do usuário que avalia (contratante avalia prestador ou vice-versa).</summary>
        public int AvaliadorId { get; set; }

        /// <summary>ID do usuário sendo avaliado.</summary>
        public int AvaliadoId { get; set; }

        /// <summary>Nota de 1 a 5. Null enquanto o avaliador ainda não enviou.</summary>
        public int? Nota { get; set; }

        /// <summary>Comentário opcional (máx. 1000 caracteres). Null até envio.</summary>
        public string? Comentario { get; set; }

        /// <summary>
        /// Escolha de privacidade do avaliador. True = o comentário pode aparecer publicamente
        /// no perfil do avaliado (se o avaliado também permitir exibição pública de avaliações).
        /// </summary>
        public bool Publica { get; set; } = false;

        /// <summary>Estado do ciclo de vida da avaliação.</summary>
        public StatusAvaliacao Status { get; set; } = StatusAvaliacao.Pendente;

        /// <summary>Preenchida quando a avaliação é publicada (blind review liberado).</summary>
        public DateTime? PublicadaEm { get; set; }

        /// <summary>Momento em que o sistema criou o slot de avaliação (na liberação do pagamento).</summary>
        public DateTime CriadoEm { get; set; }

        // ── Resposta pública (melhoria #2) ──────────────────────────────────────
        /// <summary>Resposta do avaliado à avaliação recebida (máx 500 chars, apenas 1 vez, só em publicadas).</summary>
        public string? RespostaPublica { get; set; }
        public DateTime? RespostaPublicaCriadaEm { get; set; }

        // ── Sub-critérios (melhoria #7 — schema-only, sem UI) ───────────────────
        public byte? NotaPrazo { get; set; }
        public byte? NotaComunicacao { get; set; }
        public byte? NotaQualidade { get; set; }

        // ── Navegações ──────────────────────────────────────────────────────────
        public ContratoServico ContratoServico { get; set; } = null!;
        public Usuario Avaliador { get; set; } = null!;
        public Usuario Avaliado { get; set; } = null!;
    }
}
