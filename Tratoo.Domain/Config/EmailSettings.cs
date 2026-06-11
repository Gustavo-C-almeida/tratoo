namespace Tratoo.Domain.Config
{
    /// <summary>
    /// Configurações de envio de e-mail (SMTP). Vinculadas a partir da seção "Email"
    /// do appsettings/variáveis de ambiente — NUNCA hardcoded no código-fonte.
    /// </summary>
    public class EmailSettings
    {
        public string Remetente { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
        public string ServidorSmtp { get; set; } = "smtp.gmail.com";
        public int PortaSmtp { get; set; } = 587;
    }
}
