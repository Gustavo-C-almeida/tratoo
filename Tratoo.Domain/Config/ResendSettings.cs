namespace Tratoo.Domain.Config
{
    /// <summary>
    /// Configurações de envio de e-mail transacional via SDK oficial do Resend
    /// (pacote NuGet "Resend"), que fala HTTPS com a API do Resend. Substitui o
    /// antigo SMTP/Gmail: o plano Trial do Railway bloqueia SMTP outbound (portas
    /// 25/465/587), mas HTTPS/443 funciona normalmente.
    ///
    /// Vinculadas em Program.cs a partir da seção "Resend" do appsettings, com as
    /// variáveis de ambiente planas (RESEND_API_KEY, RESEND_FROM_EMAIL,
    /// RESEND_FROM_NAME) tendo precedência — é assim que o Railway injeta valores.
    /// A ApiKey NUNCA deve ser versionada: no appsettings ficam apenas placeholders.
    ///
    /// ApiKey e BaseUrl/TimeoutSegundos são consumidos apenas em Program.cs, para
    /// configurar o SDK (AddResend + ConfigureHttpClient). ResendEmailService só
    /// precisa de FromEmail/FromName para montar o remetente.
    /// </summary>
    public class ResendSettings
    {
        /// <summary>API key do Resend (re_...). Origem: variável de ambiente RESEND_API_KEY.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Remetente. Precisa pertencer a um domínio verificado no painel do Resend.</summary>
        public string FromEmail { get; set; } = string.Empty;

        /// <summary>Nome exibido como remetente.</summary>
        public string FromName { get; set; } = "Tratoo";

        /// <summary>Base da API do Resend. Configurável apenas para testes/mocks.</summary>
        public string BaseUrl { get; set; } = "https://api.resend.com";

        /// <summary>Timeout das chamadas HTTP à API, em segundos.</summary>
        public int TimeoutSegundos { get; set; } = 15;
    }
}
