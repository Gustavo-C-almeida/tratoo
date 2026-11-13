using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// Um único servidor HTTP local fazendo o papel de TODOS os serviços externos da API real
    /// nos testes de segurança. Nenhuma chamada sai da máquina:
    ///
    ///  • <c>/resend/emails</c> ou <c>/emails</c> (o SDK ignora o caminho da BaseUrl) → Resend. Guarda cada e-mail: é de onde os testes leem os
    ///                                  códigos de MFA, cadastro e reautenticação.
    ///  • <c>/openai/v1/embeddings</c> → OpenAI. Conta as chamadas (prova de custo da busca pública).
    ///  • <c>/asaas/api/v3/*</c>      → Asaas. Responde um JSON genérico de sucesso.
    ///  • qualquer outro caminho     → R2/S3 (path style: <c>/{bucket}/{chave}</c>). Guarda as
    ///                                  chaves dos objetos gravados (prova de upload sem autorização).
    /// </summary>
    public sealed class ServidorExternoFalso : IAsyncDisposable
    {
        public sealed record EmailCapturado(string Para, string Assunto, string Texto, DateTime Em);

        private readonly WebApplication _app;
        private readonly ConcurrentQueue<EmailCapturado> _emails = new();
        private readonly ConcurrentQueue<string> _objetosGravados = new();
        private readonly ConcurrentQueue<string> _objetosRemovidos = new();
        private readonly ConcurrentQueue<string> _chamadasAsaas_ = new();
        private int _chamadasEmbeddings;
        private int _chamadasAsaas;

        public string BaseUrl { get; private set; } = "";
        public int ChamadasEmbeddings => Volatile.Read(ref _chamadasEmbeddings);
        public int ChamadasAsaas => Volatile.Read(ref _chamadasAsaas);
        public IReadOnlyCollection<string> ObjetosGravados => _objetosGravados.ToArray();
        public IReadOnlyCollection<string> ObjetosRemovidos => _objetosRemovidos.ToArray();

        /// <summary>"MÉTODO caminho" de cada chamada ao Asaas falso (ex.: "POST /asaas/api/v3/transfers").</summary>
        public IReadOnlyCollection<string> ChamadasAsaasDetalhadas => _chamadasAsaas_.ToArray();

        /// <summary>
        /// Upload ambíguo: quando o predicado casa com o caminho do PUT, o objeto É gravado, mas a
        /// resposta é 500 — como um R2 que gravou e perdeu a resposta. Null = R2 normal.
        /// </summary>
        public Func<string, bool>? GravarEResponderErro { get; set; }

        private ServidorExternoFalso(WebApplication app) => _app = app;

        public static async Task<ServidorExternoFalso> IniciarAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var falso = new ServidorExternoFalso(app);

            app.Run(falso.AtenderAsync);
            await app.StartAsync();

            var endereco = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            falso.BaseUrl = endereco.TrimEnd('/');
            return falso;
        }

        private async Task AtenderAsync(HttpContext http)
        {
            var caminho = http.Request.Path.Value ?? "/";
            string corpo;
            using (var leitor = new StreamReader(http.Request.Body))
                corpo = await leitor.ReadToEndAsync();

            if (caminho.StartsWith("/resend/", StringComparison.Ordinal) || caminho == "/emails")
            {
                RegistrarEmail(corpo);
                await http.Response.WriteAsJsonAsync(new { id = Guid.NewGuid().ToString() });
                return;
            }

            if (caminho.StartsWith("/openai/", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _chamadasEmbeddings);
                await ResponderEmbeddingsAsync(http, corpo);
                return;
            }

            if (caminho.StartsWith("/asaas/", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _chamadasAsaas);
                _chamadasAsaas_.Enqueue($"{http.Request.Method} {caminho}");
                await http.Response.WriteAsJsonAsync(new
                {
                    @object = "payment", id = "pay_falso", status = "PENDING", value = 0,
                    totalCount = 0, data = Array.Empty<object>(), hasMore = false
                });
                return;
            }

            // R2 / S3
            if (HttpMethods.IsPut(http.Request.Method) || HttpMethods.IsPost(http.Request.Method))
            {
                _objetosGravados.Enqueue(caminho);
                if (GravarEResponderErro?.Invoke(caminho) == true)
                {
                    http.Response.StatusCode = 500;
                    return;
                }
            }
            if (HttpMethods.IsDelete(http.Request.Method))
                _objetosRemovidos.Enqueue(caminho);
            http.Response.Headers.ETag = "\"etag-falso\"";
            http.Response.StatusCode = HttpMethods.IsDelete(http.Request.Method) ? 204 : 200;
        }

        private void RegistrarEmail(string corpo)
        {
            try
            {
                using var doc = JsonDocument.Parse(corpo);
                var raiz = doc.RootElement;
                var para = raiz.TryGetProperty("to", out var to)
                    ? (to.ValueKind == JsonValueKind.Array ? string.Join(",", to.EnumerateArray().Select(e => e.GetString())) : to.GetString() ?? "")
                    : "";
                var assunto = raiz.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
                var texto = raiz.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(texto) && raiz.TryGetProperty("html", out var h)) texto = h.GetString() ?? "";
                _emails.Enqueue(new EmailCapturado(para, assunto, texto, DateTime.UtcNow));
            }
            catch (JsonException)
            {
                _emails.Enqueue(new EmailCapturado("", "", corpo, DateTime.UtcNow));
            }
        }

        private static async Task ResponderEmbeddingsAsync(HttpContext http, string corpo)
        {
            var quantidade = 1;
            try
            {
                using var doc = JsonDocument.Parse(corpo);
                if (doc.RootElement.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Array)
                    quantidade = Math.Max(1, input.GetArrayLength());
            }
            catch (JsonException) { }

            // 1536 floats (text-embedding-3-small), em base64 como a API pede.
            var vetor = new float[1536];
            vetor[0] = 1f;
            var bytes = new byte[vetor.Length * sizeof(float)];
            Buffer.BlockCopy(vetor, 0, bytes, 0, bytes.Length);
            var base64 = Convert.ToBase64String(bytes);

            await http.Response.WriteAsJsonAsync(new
            {
                @object = "list",
                data = Enumerable.Range(0, quantidade).Select(i => new { @object = "embedding", index = i, embedding = base64 }),
                model = "text-embedding-3-small"
            });
        }

        /// <summary>
        /// Último código de 6 dígitos enviado para <paramref name="email"/> depois de <paramref name="desde"/>.
        /// Espera até 10 s: o envio é assíncrono em relação à resposta HTTP em alguns fluxos.
        /// </summary>
        public async Task<string> CodigoEnviadoParaAsync(string email, DateTime desde)
        {
            var limite = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < limite)
            {
                var ultimo = _emails
                    .Where(e => e.Em >= desde && e.Para.Contains(email, StringComparison.OrdinalIgnoreCase))
                    .LastOrDefault();
                if (ultimo is not null)
                {
                    var m = Regex.Match(ultimo.Texto, @"\b(\d{6})\b");
                    if (m.Success) return m.Groups[1].Value;
                }
                await Task.Delay(100);
            }
            throw new TimeoutException($"Nenhum código enviado para {email} pelo Resend falso.");
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
