using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models.Financeiro
{
    /// <summary>
    /// Ledger financeiro imutável. Cada evento monetário gera uma entrada aqui.
    /// Nunca é editado ou deletado — serve como trilha de auditoria completa.
    /// </summary>
    public class LedgerFinanceiro
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PagamentoId { get; set; }
        public Pagamento Pagamento { get; set; } = null!;

        public TipoEntradaLedger Tipo { get; set; }

        /// <summary>
        /// Valor da movimentação em BRL.
        /// Positivo = entrada de valor (cobrança paga, por exemplo).
        /// Negativo = saída de valor (taxa, liberação ao prestador).
        /// </summary>
        public decimal Valor { get; set; }

        public string Descricao { get; set; } = string.Empty;

        /// <summary>ID de referência no gateway (ex: pay_..., tra_...).</summary>
        public string? ReferenciaExterna { get; set; }

        /// <summary>Registro imutável — a data registra quando o evento ocorreu no sistema.</summary>
        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

        /// <summary>ID do usuário que disparou a ação. 0 = sistema/automático.</summary>
        public int CriadoPorId { get; set; }
    }
}
