namespace Tratoo.Domain.Features.Auth
{
    public class LoginDTO
    {
        public string Email { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;

        /// <summary>IP do cliente — preenchido pela camada de API para auditoria.</summary>
        public string Ip { get; set; } = string.Empty;
    }
}
