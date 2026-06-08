using Tratoo.Domain.Enums;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Pagamentos
{
    // ──────────────────────────────────────────────────────────────────────────
    // REQUEST
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Inicia o pagamento de um contrato ativo.</summary>
    public record IniciarPagamentoDto(Guid ContratoServicoId);

    /// <summary>Solicita liberação manual do escrow ao prestador.</summary>
    public record LiberarPagamentoDto(string? ObservacaoContratante);

    /// <summary>Abre uma disputa sobre um pagamento retido.</summary>
    public record AbrirDisputaDto(string Motivo, List<string>? Evidencias);

    /// <summary>Resolve uma disputa (uso exclusivo de administradores).</summary>
    public record ResolverDisputaDto(
        bool FavorContratante,
        string NotaResolucao
    );

    // ──────────────────────────────────────────────────────────────────────────
    // RESPONSE
    // ──────────────────────────────────────────────────────────────────────────

    public class PagamentoPixDto
    {
        public Guid PagamentoId { get; set; }
        public StatusPagamento Status { get; set; }
        public decimal ValorBruto { get; set; }

        /// <summary>Payload copia-e-cola do PIX.</summary>
        public string? PixPayload { get; set; }

        /// <summary>Imagem base64 do QR Code (data URI).</summary>
        public string? PixQrCodeImagem { get; set; }

        /// <summary>Data/hora de expiração do QR Code.</summary>
        public DateTime? PixExpiracao { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime? LiberacaoAutomaticaEm { get; set; }
    }

    public class PagamentoResumoDto
    {
        public Guid Id { get; set; }
        public Guid? ContratoServicoId { get; set; }
        public StatusPagamento Status { get; set; }
        public decimal ValorBruto { get; set; }
        public MetodoPagamento Metodo { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? PagoEm { get; set; }
        public DateTime? LiberadoEm { get; set; }
        public DateTime? LiberacaoAutomaticaEm { get; set; }
        public bool EmDisputa { get; set; }
    }

    public class PagamentoDetalheDto : PagamentoResumoDto
    {
        public string? PixPayload { get; set; }
        public string? PixQrCodeImagem { get; set; }
        public DateTime? PixExpiracao { get; set; }
        public string? AsaasCobrancaId { get; set; }
        public string? StatusGateway { get; set; }

        /// <summary>
        /// Taxa operacional do gateway (Asaas). Exclusivamente informativo — não afeta
        /// nenhum cálculo. Exibido apenas em contexto administrativo/auditoria.
        /// </summary>
        public decimal? TaxaGateway { get; set; }

        public List<LedgerEntradaDto> Ledger { get; set; } = [];
        public List<DisputaResumoDto> Disputas { get; set; } = [];
    }

    public class LedgerEntradaDto
    {
        public Guid Id { get; set; }
        public TipoEntradaLedger Tipo { get; set; }
        public decimal Valor { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public string? ReferenciaExterna { get; set; }
        public DateTime CriadoEm { get; set; }
    }

    public class DisputaResumoDto
    {
        public Guid Id { get; set; }
        public StatusDisputa Status { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public DateTime AbertoEm { get; set; }
        public DateTime? ResolvidaEm { get; set; }
        public string? NotaResolucao { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ASAAS GATEWAY INTERNOS (trafegam entre Service e GatewayService)
    // ──────────────────────────────────────────────────────────────────────────

    public record AsaasClienteRequest(
        int UsuarioId,
        string Nome,
        string CpfCnpj,
        string Email
    );

    public record AsaasCobrancaRequest(
        string AsaasClienteId,
        decimal Valor,
        string Descricao,
        string ReferenciaExterna,
        DateTime DataVencimento
    );

    public record AsaasCobrancaResponse(
        string Id,
        string Status,
        string? InvoiceUrl
    );

    public record AsaasPixQrCodeResponse(
        string Payload,
        string? EncodedImage,
        DateTime? ExpirationDate
    );

    public record AsaasTransferenciaPixRequest(
        decimal Valor,
        string ChavePix,
        string TipoChavePix,
        string Descricao
    );

    public record AsaasTransferenciaResponse(
        string Id,
        string Status
    );
}
