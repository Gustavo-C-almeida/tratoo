using Tratoo.Domain.Enums;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Models
{
    public class Pagamento
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // ── Relacionamentos ────────────────────────────────────────────────────
        /// <summary>FK principal para o fluxo atual (ContratoServico).</summary>
        public Guid? ContratoServicoId { get; set; }
        public ContratoServico? ContratoServico { get; set; }

        /// <summary>FK legado mantida para compatibilidade com Contrato antigo.</summary>
        public Guid? ContratoId { get; set; }
        public Contrato? Contrato { get; set; }

        // ── Valores ───────────────────────────────────────────────────────────
        public decimal ValorBruto { get; set; }

        /// <summary>Taxa da plataforma Tratoo (~10%). Descontada do valor bruto.</summary>
        public decimal TaxaPlataforma { get; set; }

        /// <summary>
        /// Taxa cobrada pelo Asaas (registrada separadamente para rastreabilidade).
        /// </summary>
        public decimal TaxaGateway { get; set; }

        /// <summary>Valor líquido que o prestador receberá: ValorBruto - TaxaPlataforma.</summary>
        public decimal ValorLiquidoPrestador => ValorBruto - TaxaPlataforma;

        // ── Status ────────────────────────────────────────────────────────────
        public StatusPagamento Status { get; set; } = StatusPagamento.Criado;

        // ── Idempotência ──────────────────────────────────────────────────────
        /// <summary>Chave única para evitar criação de cobranças duplicadas no gateway.</summary>
        public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString();

        // ── Datas ─────────────────────────────────────────────────────────────
        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
        public DateTime? PagoEm { get; set; }
        public DateTime? LiberadoEm { get; set; }
        public DateTime? EstornadoEm { get; set; }

        /// <summary>
        /// Data limite para liberação automática ao prestador.
        /// Calculado como: PrazoEntrega do contrato + 7 dias de carência.
        /// </summary>
        public DateTime? LiberacaoAutomaticaEm { get; set; }

        // ── Gateway (Asaas) ───────────────────────────────────────────────────
        public MetodoPagamento Metodo { get; set; } = MetodoPagamento.Pix;

        /// <summary>Nome do provedor de pagamento. Ex: "Asaas".</summary>
        public string Gateway { get; set; } = "Asaas";

        /// <summary>ID da cobrança no Asaas (pay_...).</summary>
        public string? GatewayPagamentoId { get; set; }

        /// <summary>ID do cliente no Asaas vinculado ao contratante (cus_...).</summary>
        public string? AsaasClienteId { get; set; }

        /// <summary>ID da transferência PIX criada no Asaas para pagar o prestador (tra_...).</summary>
        public string? AsaasTransferenciaId { get; set; }

        /// <summary>Status retornado pelo gateway (ex: PENDING, RECEIVED, CONFIRMED).</summary>
        public string? StatusGateway { get; set; }

        // ── PIX ───────────────────────────────────────────────────────────────
        /// <summary>Payload copia-e-cola do PIX (EMV BR Code).</summary>
        public string? PixQrCodePayload { get; set; }

        /// <summary>Imagem em base64 do QR Code PIX (PNG).</summary>
        public string? PixQrCodeImagem { get; set; }

        /// <summary>Data/hora de expiração do QR Code.</summary>
        public DateTime? PixQrCodeExpiracao { get; set; }

        // ── Relacionamentos financeiros ────────────────────────────────────────
        public ICollection<DisputaPagamento> Disputas { get; set; } = new List<DisputaPagamento>();
        public ICollection<LedgerFinanceiro> Ledger { get; set; } = new List<LedgerFinanceiro>();

        // ── Auditoria ─────────────────────────────────────────────────────────
        /// <summary>Último payload de webhook recebido (para debug e auditoria).</summary>
        public string? PayloadGateway { get; set; }
    }
}
