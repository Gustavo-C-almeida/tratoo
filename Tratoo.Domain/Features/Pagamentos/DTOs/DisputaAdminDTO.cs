using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Pagamentos
{
    /// <summary>Item da listagem administrativa de disputas.</summary>
    public class DisputaAdminResumoDto
    {
        public Guid DisputaId { get; set; }
        public Guid PagamentoId { get; set; }
        public StatusDisputa Status { get; set; }
        public DateTime AbertoEm { get; set; }
        public DateTime? ResolvidaEm { get; set; }
        public string Motivo { get; set; } = string.Empty;

        public int ProjetoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;

        public int ContratanteId { get; set; }
        public string ContratanteNome { get; set; } = string.Empty;

        public int PrestadorId { get; set; }
        public string PrestadorNome { get; set; } = string.Empty;

        /// <summary>Valor negociado (bruto do pagamento em escrow).</summary>
        public decimal ValorNegociado { get; set; }
    }

    /// <summary>Entrada de auditoria/histórico exibida na tela de detalhe.</summary>
    public class HistoricoEventoDto
    {
        public string Acao { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public int UsuarioId { get; set; }
        public DateTime DataEvento { get; set; }
    }

    /// <summary>Detalhe completo de uma disputa para análise administrativa.</summary>
    public class DisputaAdminDetalheDto : DisputaAdminResumoDto
    {
        public int AbertoPorId { get; set; }
        public string? NotaResolucao { get; set; }
        public int? ResolvidoPorId { get; set; }

        /// <summary>Descrição detalhada / motivo informado pelo solicitante.</summary>
        public string Descricao { get; set; } = string.Empty;

        /// <summary>Evidências anexadas (textos/URLs).</summary>
        public List<string> Evidencias { get; set; } = [];

        // ── Estado atual ──────────────────────────────────────────────────────
        public StatusPagamento StatusPagamento { get; set; }
        public ContratoServicoStatus? StatusContrato { get; set; }

        // ── Contexto do projeto/contrato ──────────────────────────────────────
        public string ProjetoDescricao { get; set; } = string.Empty;
        public Guid? ContratoId { get; set; }
        public DateTime? ContratoCriadoEm { get; set; }

        // ── Histórico (auditoria) ─────────────────────────────────────────────
        public List<HistoricoEventoDto> HistoricoContrato { get; set; } = [];
        public List<LedgerEntradaDto> Ledger { get; set; } = [];
    }

    /// <summary>Payload de resolução administrativa de disputa.</summary>
    public record ResolverDisputaAdminRequest(bool FavorContratante, string NotaResolucao);
}
