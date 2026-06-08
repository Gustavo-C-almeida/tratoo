using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Comprovação formal da entrega de um serviço pelo prestador.
    /// Vinculada a um ContratoServico, contém descrição, evidências (anexos no R2
    /// privado), links externos e o ciclo de aprovação/rejeição pelo contratante.
    /// </summary>
    public class Entrega
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // ── Origem ────────────────────────────────────────────────────────────────
        public Guid ContratoServicoId { get; set; }
        public ContratoServico ContratoServico { get; set; } = null!;

        // ── Conteúdo ──────────────────────────────────────────────────────────────
        public string DescricaoEntrega { get; set; } = string.Empty;
        public string? Observacoes { get; set; }
        public DateTime DataEntrega { get; set; }

        // ── Status ────────────────────────────────────────────────────────────────
        public EntregaStatus Status { get; set; } = EntregaStatus.PendenteAprovacao;

        // ── Aprovação ─────────────────────────────────────────────────────────────
        public DateTime? AprovadaEm { get; set; }
        /// <summary>UserId do contratante que aprovou a entrega.</summary>
        public int? AprovadorId { get; set; }

        // ── Rejeição ──────────────────────────────────────────────────────────────
        public string? MotivoRejeicao { get; set; }
        public DateTime? RejeitadaEm { get; set; }

        // ── Datas ─────────────────────────────────────────────────────────────────
        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
        public DateTime? AtualizadoEm { get; set; }

        // ── Navegações ────────────────────────────────────────────────────────────
        public ICollection<EntregaAnexo> Anexos { get; set; } = new List<EntregaAnexo>();
        public ICollection<EntregaLink> Links { get; set; } = new List<EntregaLink>();
    }
}
