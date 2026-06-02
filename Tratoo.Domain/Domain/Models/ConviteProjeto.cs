using Tratoo.Domain.Enums;
using Tratoo.Domain.Models.Prestador;

namespace Tratoo.Domain.Models
{
    public class ConviteProjeto
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public int ProjetoId { get; set; }
        public Projeto Projeto { get; set; } = null!;

        public int ContratanteId { get; set; }
        public Contratante Contratante { get; set; } = null!;

        public int PrestadorId { get; set; }
        public Prestador.Prestador Prestador { get; set; } = null!;

        public string MensagemInicial { get; set; } = string.Empty;

        public decimal? OrcamentoSugerido { get; set; }
        public DateTime? PrazoDesejado { get; set; }

        public StatusConvite Status { get; set; } = StatusConvite.Pendente;

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
        public DateTime? RespondidoEm { get; set; }
        public string? MotivoRecusa { get; set; }
    }
}
