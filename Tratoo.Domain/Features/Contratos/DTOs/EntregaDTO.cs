using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Contratos
{
    // ── Entrada: registrar entrega ──────────────────────────────────────────────
    // Os arquivos chegam separadamente (multipart). Este DTO carrega os campos texto.

    public record RegistrarEntregaDto(
        string DescricaoEntrega,
        string? Observacoes,
        DateTime DataEntrega,
        List<EntregaLinkDto>? Links);

    public record EntregaLinkDto(string Url, string? Descricao);

    /// <summary>Metadados de um anexo já enviado ao R2 (preenchido pelo endpoint).</summary>
    public record EntregaAnexoUpload(
        string NomeArquivo,
        string ChaveR2,
        string TipoArquivo,
        long TamanhoArquivo);

    public record RejeitarEntregaDto(string Motivo);

    // ── Saída ───────────────────────────────────────────────────────────────────

    public class EntregaDetalheDto
    {
        public Guid Id { get; set; }
        public Guid ContratoServicoId { get; set; }
        public string DescricaoEntrega { get; set; } = string.Empty;
        public string? Observacoes { get; set; }
        public DateTime DataEntrega { get; set; }
        public EntregaStatus Status { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? AprovadaEm { get; set; }
        public DateTime? RejeitadaEm { get; set; }
        public string? MotivoRejeicao { get; set; }
        public List<EntregaAnexoDto> Anexos { get; set; } = [];
        public List<EntregaLinkDetalheDto> Links { get; set; } = [];
    }

    public class EntregaAnexoDto
    {
        public Guid Id { get; set; }
        public string NomeArquivo { get; set; } = string.Empty;
        public string TipoArquivo { get; set; } = string.Empty;
        public long TamanhoArquivo { get; set; }
        /// <summary>URL pré-assinada temporária para download do bucket privado.</summary>
        public string UrlDownload { get; set; } = string.Empty;
        public DateTime CriadoEm { get; set; }
    }

    public class EntregaLinkDetalheDto
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string? Descricao { get; set; }
    }

    public class HistoricoContratoDto
    {
        public AcaoHistoricoContrato Acao { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public int UsuarioId { get; set; }
        public DateTime DataEvento { get; set; }
    }
}
