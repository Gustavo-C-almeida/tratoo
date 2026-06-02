namespace Tratoo.Domain.Features.Auth
{
    public class ResetarSenhaDTO
    {
        public string Email { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string NovaSenha { get; set; } = string.Empty;
        public string ConfirmarNovaSenha { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
    }
}
