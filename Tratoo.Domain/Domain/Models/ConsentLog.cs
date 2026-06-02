using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Registro imutável de consentimento do usuário — exigido pela LGPD Art. 7.
    /// Nunca deve ser deletado, mesmo após exclusão da conta.
    /// </summary>
    public class ConsentLog
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public TipoConsentimento Tipo { get; set; }

        /// <summary>Versão do documento aceito (ex: "v1.0", "2024-01").</summary>
        public string Versao { get; set; } = string.Empty;

        /// <summary>IP do usuário no momento do aceite.</summary>
        public string Ip { get; set; } = string.Empty;

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    }
}
