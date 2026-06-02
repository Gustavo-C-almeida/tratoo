using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Mensagens
{
    /// <summary>Resumo do convite — usado em listagens.</summary>
    public class ConviteProjetoDTO
    {
        public Guid Id { get; set; }
        public int ProjetoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        public StatusProjeto ProjetoStatus { get; set; }
        public decimal OrcamentoMin { get; set; }
        public decimal OrcamentoMax { get; set; }

        public int ContratanteId { get; set; }
        public string ContratanteNome { get; set; } = string.Empty;

        public int PrestadorId { get; set; }
        public string PrestadorNome { get; set; } = string.Empty;

        public string MensagemInicial { get; set; } = string.Empty;
        public decimal? OrcamentoSugerido { get; set; }
        public DateTime? PrazoDesejado { get; set; }

        public StatusConvite Status { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? RespondidoEm { get; set; }
        public string? MotivoRecusa { get; set; }
    }

    /// <summary>DTO de entrada para criar convite.</summary>
    public class CriarConviteProjetoDTO
    {
        public int ProjetoId { get; set; }
        public int ContratanteId { get; set; }
        public int PrestadorId { get; set; }
        public string MensagemInicial { get; set; } = string.Empty;
        public decimal? OrcamentoSugerido { get; set; }
        public DateTime? PrazoDesejado { get; set; }
    }
}
