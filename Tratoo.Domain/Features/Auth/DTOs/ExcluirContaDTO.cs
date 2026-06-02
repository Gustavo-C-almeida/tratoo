namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Solicitação de exclusão de conta (direito ao esquecimento — LGPD Art. 18).
    /// </summary>
    public class ExcluirContaDTO
    {
        public int UserId { get; set; }

        /// <summary>IP do cliente — para registro de auditoria.</summary>
        public string Ip { get; set; } = string.Empty;
    }
}
