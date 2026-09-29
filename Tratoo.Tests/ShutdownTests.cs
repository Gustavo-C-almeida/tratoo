using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Encerramento gracioso. O sinal SIGTERM em si (PID 1, ENTRYPOINT em forma exec)
    /// só é verificável no container; aqui fica o que é determinístico em processo:
    /// o prazo configurado, a coerência com o railway.toml, a drenagem de uma
    /// requisição em andamento e o registro de que o encerramento terminou.
    /// </summary>
    public class ShutdownTests
    {
        private sealed class LoggerEmMemoria : ILoggerProvider, ILogger
        {
            public ConcurrentQueue<string> Mensagens { get; } = new();
            public ILogger CreateLogger(string categoryName) => this;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
                => Mensagens.Enqueue(formatter(state, exception));
            public void Dispose() { }
        }

        private static int PortaLivre()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        [Fact]
        public void Host_usa_o_prazo_de_encerramento_do_ShutdownSetup()
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddTratooShutdown();

            var opcoes = services.BuildServiceProvider().GetRequiredService<IOptions<HostOptions>>().Value;

            Assert.Equal(ShutdownSetup.TimeoutEncerramento, opcoes.ShutdownTimeout);
        }

        [Fact]
        public void Prazo_do_host_cabe_no_drainingSeconds_da_Railway_com_margem()
        {
            // A Railway manda SIGKILL drainingSeconds depois do SIGTERM. Se o host .NET
            // puder levar mais que isso, o processo morre no meio do encerramento.
            var drainingSeconds = LerDrainingSecondsDoRailwayToml();
            var margem = TimeSpan.FromSeconds(drainingSeconds) - ShutdownSetup.TimeoutEncerramento;

            Assert.True(margem >= TimeSpan.FromSeconds(5),
                $"drainingSeconds={drainingSeconds}s precisa exceder ShutdownTimeout=" +
                $"{ShutdownSetup.TimeoutEncerramento.TotalSeconds}s em pelo menos 5s " +
                "(descarte de conexões e flush de log depois que o host para).");
        }

        [Fact]
        public async Task Requisicao_em_andamento_termina_antes_do_host_parar_e_o_fim_fica_registrado()
        {
            var porta = PortaLivre();
            var logs = new LoggerEmMemoria();
            var entrouNoHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PORT"] = porta.ToString(CultureInfo.InvariantCulture)
            });
            builder.WebHost.UsarPortaDaPlataforma(builder.Configuration);
            builder.Services.AddTratooShutdown();

            await using var app = builder.Build();
            app.UseLogDeEncerramento();
            app.MapGet("/lenta", async () =>
            {
                entrouNoHandler.SetResult();
                // Continua trabalhando DEPOIS do pedido de parada; não observa o
                // RequestAborted de propósito — é o caso de uma operação já iniciada.
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                return Results.Ok("concluida");
            });

            await app.StartAsync();

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var requisicao = client.GetAsync($"http://localhost:{porta}/lenta");
            await entrouNoHandler.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Equivalente ao que o ConsoleLifetime faz ao receber SIGTERM.
            var parada = app.StopAsync();

            using var resposta = await requisicao;
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.Equal("\"concluida\"", await resposta.Content.ReadAsStringAsync());

            await parada.WaitAsync(ShutdownSetup.TimeoutEncerramento);

            // Depois de parado, não aceita conexão nova.
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetAsync($"http://localhost:{porta}/lenta"));

            Assert.Contains(logs.Mensagens, m => m.StartsWith("Encerramento concluído em"));
        }

        /// <summary>
        /// Lê <c>drainingSeconds</c> da seção [deploy] do railway.toml na raiz do repositório.
        /// </summary>
        private static int LerDrainingSecondsDoRailwayToml()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "railway.toml")))
                dir = dir.Parent;

            Assert.NotNull(dir);
            var toml = File.ReadAllText(Path.Combine(dir!.FullName, "railway.toml"));

            var secaoDeploy = Regex.Match(toml, @"^\[deploy\]\s*$(?<corpo>.*?)(?=^\[|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.True(secaoDeploy.Success, "railway.toml sem seção [deploy].");

            var chave = Regex.Match(secaoDeploy.Groups["corpo"].Value,
                @"^\s*drainingSeconds\s*=\s*(?<valor>\d+)\s*(#.*)?$", RegexOptions.Multiline);
            Assert.True(chave.Success, "railway.toml sem drainingSeconds em [deploy] — o default da Railway é 0s.");

            return int.Parse(chave.Groups["valor"].Value, CultureInfo.InvariantCulture);
        }
    }
}
