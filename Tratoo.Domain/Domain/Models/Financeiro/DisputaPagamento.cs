namespace Tratoo.Domain.Models.Financeiro
{
    public enum StatusDisputa
    {
        Aberta,
        EmAnalise,
        ResolvidaAFavorContratante,
        ResolvidaAFavorPrestador,
        Encerrada
    }

    /// <summary>
    /// Representa uma disputa aberta sobre um pagamento retido em escrow.
    /// Resolvida manualmente por um administrador da plataforma.
    /// </summary>
    public class DisputaPagamento
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PagamentoId { get; set; }
        public Pagamento Pagamento { get; set; } = null!;

        public StatusDisputa Status { get; set; } = StatusDisputa.Aberta;

        /// <summary>ID do usuário (contratante ou prestador) que abriu a disputa.</summary>
        public int AbertoPorId { get; set; }

        public DateTime AbertoEm { get; set; } = DateTime.UtcNow;

        /// <summary>Motivo da disputa informado pelo solicitante.</summary>
        public string Motivo { get; set; } = string.Empty;

        /// <summary>Evidências em JSON (array de textos ou URLs).</summary>
        public string? EvidenciasJson { get; set; }

        // ── Resolução ──────────────────────────────────────────────────────────
        /// <summary>ID do administrador que resolveu a disputa.</summary>
        public int? ResolvidoPorId { get; set; }

        public DateTime? ResolvidaEm { get; set; }

        /// <summary>Nota explicativa da decisão.</summary>
        public string? NotaResolucao { get; set; }
    }
}
