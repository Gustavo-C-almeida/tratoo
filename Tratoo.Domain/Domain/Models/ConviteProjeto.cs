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

        /// <summary>
        /// Convite pendente vence 30 dias após o envio, ou quando o projeto deixa de estar Aberto
        /// (contratado, cancelado, encerrado). Convite aceito não expira. Usa CriadoEm, que já
        /// existe: nenhuma coluna nova.
        /// </summary>
        public static readonly TimeSpan ValidadePendente = TimeSpan.FromDays(30);

        /// <summary>Pendente e vencido (pelo prazo ou pelo projeto). Exige Projeto carregado para a regra do projeto.</summary>
        public bool PendenteVencido(DateTime agoraUtc) =>
            Status == StatusConvite.Pendente &&
            (CriadoEm <= agoraUtc - ValidadePendente || (Projeto is not null && Projeto.Status != StatusProjeto.Aberto));
    }
}
