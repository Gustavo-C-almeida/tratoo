namespace Tratoo.Domain.Models
{
    public enum AcaoHistoricoAssinatura
    {
        OtpSolicitado,
        OtpValidado,
        OtpFalhaValidacao,
        OtpBloqueadoBruteForce,
        Assinado,
        ContratoBloqueadoAssinado,
    }

    /// <summary>
    /// Trilha de auditoria imutável para cada evento do processo de assinatura digital.
    /// Cada linha é append-only — nunca atualizada após inserção.
    /// </summary>
    public class HistoricoAssinatura
    {
        public int Id { get; set; }

        public Guid ContratoId { get; set; }
        public ContratoServico Contrato { get; set; } = null!;

        public int UsuarioId { get; set; }

        public AcaoHistoricoAssinatura Acao { get; set; }

        /// <summary>IP do usuário no momento do evento.</summary>
        public string Ip { get; set; } = string.Empty;

        /// <summary>User-Agent do navegador/cliente no momento do evento.</summary>
        public string? UserAgent { get; set; }

        public DateTime DataEvento { get; set; } = DateTime.UtcNow;
    }
}
