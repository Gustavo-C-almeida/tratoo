using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Tratoo.Domain.Features.IA
{
    public interface IEmbeddingService
    {
        Task<float[]> GerarEmbeddingAsync(string texto);
        Task<List<float[]>> GerarEmbeddingBatchAsync(List<string> textos);
        Task<bool> VerificarSaudeAsync();
    }

    /// <summary>
    /// Gera embeddings via OpenAI text-embedding-3-small (1536 dimensões).
    /// HttpClient base address: https://api.openai.com/v1/
    /// Authorization: Bearer {OpenAI:ApiKey}
    /// </summary>
    public class EmbeddingService : IEmbeddingService
    {
        private const string Model = "text-embedding-3-small";

        private readonly HttpClient _http;
        private readonly ILogger<EmbeddingService> _logger;

        public EmbeddingService(HttpClient http, ILogger<EmbeddingService> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<float[]> GerarEmbeddingAsync(string texto)
        {
            var response = await _http.PostAsJsonAsync("embeddings", new
            {
                model            = Model,
                input            = texto,
                encoding_format  = "base64"   // binário float32 — menor payload, sem perda de precisão
            });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>()
                ?? throw new InvalidOperationException("Resposta inválida da OpenAI Embeddings API.");

            return DecodeBase64Embedding(result.Data[0].EmbeddingBase64);
        }

        public async Task<List<float[]>> GerarEmbeddingBatchAsync(List<string> textos)
        {
            var response = await _http.PostAsJsonAsync("embeddings", new
            {
                model            = Model,
                input            = textos,
                encoding_format  = "base64"
            });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>()
                ?? throw new InvalidOperationException("Resposta inválida da OpenAI Embeddings API.");

            return result.Data
                .OrderBy(d => d.Index)
                .Select(d => DecodeBase64Embedding(d.EmbeddingBase64))
                .ToList();
        }

        public async Task<bool> VerificarSaudeAsync()
        {
            try
            {
                var response = await _http.PostAsJsonAsync("embeddings", new
                {
                    model = Model,
                    input = "ok",
                    encoding_format = "base64"
                });
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenAI Embeddings API indisponível.");
                return false;
            }
        }

        // ── Decodifica o blob base64 → float[] (little-endian IEEE 754 float32) ──

        private static float[] DecodeBase64Embedding(string base64)
        {
            var bytes = Convert.FromBase64String(base64);
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }

        // ── DTOs de desserialização ───────────────────────────────────────────────

        private record OpenAiEmbeddingResponse(
            [property: JsonPropertyName("data")] List<EmbeddingItem> Data);

        private record EmbeddingItem(
            [property: JsonPropertyName("index")]     int Index,
            [property: JsonPropertyName("embedding")] string EmbeddingBase64);
    }
}
