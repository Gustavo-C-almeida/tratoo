using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Tratoo.Domain.Features.Infrastructure;
using Xunit;

namespace Tratoo.Tests;

/// <summary>
/// A chave da OpenAI não pode chegar aos logs: nem inteira, pelo header Authorization,
/// nem mascarada, pelo corpo do erro 401 da própria OpenAI. O HttpClient é montado como
/// em Program.cs, porque é ele que copia os headers padrão para a requisição antes de
/// passar pelo handler.
/// </summary>
public class OpenAiLoggingHandlerTests
{
    private const string Chave = "sk-proj-chaveDeTeste1234567890";
    private const string ChaveMascarada = "sk-proj-***7890";

    [Fact]
    public async Task Requisicao_nao_loga_a_chave()
    {
        var logs = await EnviarAsync(HttpStatusCode.OK, """{"data":[]}""");

        Assert.Contains(logs, l => l.Contains("OpenAI Request: POST"));
        Assert.DoesNotContain(logs, l => l.Contains(Chave));
        Assert.DoesNotContain(logs, l => l.Contains("Bearer"));
        Assert.DoesNotContain(logs, l => l.Contains("Authorization"));
    }

    [Fact]
    public async Task Erro_401_nao_loga_a_chave_mascarada_mas_mantem_a_mensagem()
    {
        var corpo = $$$"""{"error":{"message":"Incorrect API key provided: {{{ChaveMascarada}}}. Check your task-list."}}""";

        var logs = await EnviarAsync(HttpStatusCode.Unauthorized, corpo);

        Assert.DoesNotContain(logs, l => l.Contains(ChaveMascarada));
        Assert.Contains(logs, l => l.Contains("Incorrect API key provided: [REDACTED]."));
        // "task-list" contém "sk-", mas não é chave.
        Assert.Contains(logs, l => l.Contains("task-list"));
    }

    [Fact]
    public async Task Resposta_continua_legivel_e_inalterada_para_quem_chamou()
    {
        var corpo = $$$"""{"error":{"message":"Incorrect API key provided: {{{ChaveMascarada}}}."}}""";
        var logger = new LoggerDeTeste();
        using var client = CriarCliente(logger, HttpStatusCode.Unauthorized, corpo);

        var resposta = await client.PostAsync(
            "embeddings", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(corpo, await resposta.Content.ReadAsStringAsync());
    }

    private static async Task<List<string>> EnviarAsync(HttpStatusCode status, string corpo)
    {
        var logger = new LoggerDeTeste();
        using var client = CriarCliente(logger, status, corpo);

        await client.PostAsync(
            "embeddings", new StringContent("{}", Encoding.UTF8, "application/json"));

        return logger.Mensagens;
    }

    private static HttpClient CriarCliente(LoggerDeTeste logger, HttpStatusCode status, string corpo)
    {
        var handler = new OpenAiLoggingHandler(logger)
        {
            InnerHandler = new RespostaFixa(status, corpo)
        };

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test/v1/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Chave);
        return client;
    }

    private sealed class RespostaFixa(HttpStatusCode status, string corpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(corpo, Encoding.UTF8, "application/json")
            });
    }

    private sealed class LoggerDeTeste : ILogger<OpenAiLoggingHandler>
    {
        public List<string> Mensagens { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Mensagens.Add(formatter(state, exception));
    }
}
