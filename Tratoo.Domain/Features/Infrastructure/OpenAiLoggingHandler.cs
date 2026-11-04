using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Tratoo.Domain.Features.Infrastructure
{
    /// <summary>
    /// Handler HTTP que loga automaticamente requisições e respostas da OpenAI API.
    /// Intercepta todas as chamadas antes de serem enviadas e após retornar.
    /// </summary>
    public class OpenAiLoggingHandler : DelegatingHandler
    {
        // O erro 401 da OpenAI repete um trecho da chave no corpo ("sk-abc***xyz").
        private static readonly Regex PadraoChave = new(@"\bsk-[A-Za-z0-9_*\-]+", RegexOptions.Compiled);

        private readonly ILogger<OpenAiLoggingHandler> _logger;

        public OpenAiLoggingHandler(ILogger<OpenAiLoggingHandler> logger)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Log da requisição
            var requestContent = "";
            if (request.Content != null)
            {
                requestContent = await request.Content.ReadAsStringAsync(cancellationToken);
                // Recria o content pois ReadAsStringAsync consome o stream
                request.Content = new StringContent(requestContent, Encoding.UTF8, "application/json");
            }

            // Os headers ficam de fora: o Authorization carrega a chave da API.
            _logger.LogInformation(
                "OpenAI Request: {Method} {Uri}\nBody: {Body}",
                request.Method,
                request.RequestUri,
                requestContent);

            // Envia a requisição
            var response = await base.SendAsync(request, cancellationToken);

            // Log da resposta
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentLength = responseContent.Length;

            _logger.LogInformation(
                "OpenAI Response: {StatusCode} ({ReasonPhrase})\nContent-Length: {ContentLength} bytes\nBody: {Body}",
                response.StatusCode,
                response.ReasonPhrase,
                contentLength,
                PadraoChave.Replace(responseContent, "[REDACTED]"));

            // Recria o content para que a aplicação consiga ler
            response.Content = new StringContent(responseContent, Encoding.UTF8, "application/json");

            return response;
        }
    }
}
