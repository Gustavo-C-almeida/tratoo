using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Trilha de auditoria por contrato. Registra ações relevantes do ciclo de entrega
    /// e liberação (criação, anexos, links, aprovação, rejeição, liberação de pagamento).
    /// </summary>
    public class HistoricoContrato
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ContratoServicoId { get; set; }

        public AcaoHistoricoContrato Acao { get; set; }

        public string Descricao { get; set; } = string.Empty;

        /// <summary>UserId que realizou a ação (0/sistema para ações automáticas).</summary>
        public int UsuarioId { get; set; }

        public DateTime DataEvento { get; set; } = DateTime.UtcNow;
    }
}
