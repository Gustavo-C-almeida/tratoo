namespace Tratoo.Domain.Features.Auth
{
    public record ValidarLoginMFAUserDTO
    {
        public string Email { get; init; } = string.Empty;
        public string Codigo { get; init; } = string.Empty;

        /// <summary>IP do cliente — preenchido pela camada de API para auditoria.</summary>
        public string Ip { get; init; } = string.Empty;
    }
}
