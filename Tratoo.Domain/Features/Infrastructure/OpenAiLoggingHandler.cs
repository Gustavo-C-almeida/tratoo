using System.Text;
using Microsoft.Extensions.Logging;

namespace Tratoo.Domain.Features.Infrastructure
{
    /// <summary>
    /// Handler HTTP que loga automaticamente requisições e respostas da OpenAI API.
    /// Intercepta todas as chamadas antes de serem enviadas e após retornar.
    /// </summary>
    public class OpenAiLoggingHandler : DelegatingHandler
    {
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

            _logger.LogInformation(
                "OpenAI Request: {Method} {Uri}\nHeaders: {Headers}\nBody: {Body}",
                request.Method,
                request.RequestUri,
                string.Join(", ", request.Headers.Select(h => $"{h.Key}: {h.Value.FirstOrDefault()}")),
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
                responseContent);

            // Recria o content para que a aplicação consiga ler
            response.Content = new StringContent(responseContent, Encoding.UTF8, "application/json");

            return response;
        }
    }
}
