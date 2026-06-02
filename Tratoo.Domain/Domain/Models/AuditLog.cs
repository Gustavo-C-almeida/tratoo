namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Registro de ações relevantes do usuário.
    /// Marco Civil da Internet (Art. 15) exige retenção por 6 meses.
    /// Ações a logar: login, assinatura de contrato, saque.
    /// </summary>
    public class AuditLog
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        /// <summary>Ex: "login", "contrato_assinado", "saque_solicitado"</summary>
        public string Acao { get; set; } = string.Empty;

        public string Ip { get; set; } = string.Empty;

        public DateTime DataHora { get; set; } = DateTime.UtcNow;
    }
}
