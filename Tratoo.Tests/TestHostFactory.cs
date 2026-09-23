using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using Tratoo.API.Infrastructure;

namespace Tratoo.Tests
{
    /// <summary>
    /// Sobe um host in-memory com o MESMO caminho de produção:
    /// <c>AddTratooForwardedHeaders(IConfiguration)</c> → binding real da seção
    /// "ForwardedHeaders" → <c>UseGateDePeerConfiavel()</c> → <c>UseForwardedHeaders()</c>.
    ///
    /// Nada aqui chama <c>ForwardedHeadersSetup.Aplicar()</c> diretamente: os testes
    /// passam pela leitura de configuração de verdade, que é onde uma regressão real
    /// (nome de seção errado, binding de array quebrado) apareceria.
    ///
    /// O TestServer não tem conexão TCP real, então o IP do peer é injetado pelo header
    /// <see cref="HeaderPeer"/> num middleware que roda ANTES do gate — exatamente a
    /// posição que o sistema operacional ocuparia num servidor real.
    /// </summary>
    public static class TestHostFactory
    {
        /// <summary>Header só de teste: simula o IP da conexão TCP recebida pelo Kestrel.</summary>
        public const string HeaderPeer = "X-Test-Peer";

        /// <summary>Peer dentro da faixa confiável padrão (CGNAT do Railway).</summary>
        public const string PeerConfiavel = "100.64.0.1";

        /// <summary>Peer fora de qualquer faixa confiável — simula acesso direto ao Kestrel.</summary>
        public const string PeerNaoConfiavel = "192.0.2.66";

        /// <summary>
        /// Monta o dicionário de configuração no mesmo formato que o binder enxerga.
        /// Passar <c>null</c> num parâmetro = chave ausente = vale o padrão do C#.
        /// </summary>
        public static Dictionary<string, string?> Config(
            int? forwardLimit = null,
            bool? habilitado = null,
            string[]? peersConfiaveis = null,
            string[]? knownProxies = null,
            string[]? knownNetworks = null)
        {
            var config = new Dictionary<string, string?>();

            if (forwardLimit is not null)
                config["ForwardedHeaders:ForwardLimit"] = forwardLimit.Value.ToString();

            if (habilitado is not null)
                config["ForwardedHeaders:Habilitado"] = habilitado.Value ? "true" : "false";

            AdicionarArray(config, "ForwardedHeaders:PeersConfiaveis", peersConfiaveis);
            AdicionarArray(config, "ForwardedHeaders:KnownProxies", knownProxies);
            AdicionarArray(config, "ForwardedHeaders:KnownNetworks", knownNetworks);

            return config;
        }

        private static void AdicionarArray(
            Dictionary<string, string?> config, string chave, string[]? valores)
        {
            if (valores is null)
                return;

            // Array explicitamente vazio: o binder precisa de ao menos uma chave para
            // sobrescrever o padrão. Entrada em branco é descartada por RedeConfiavel,
            // resultando em "gate desligado" — que é como se desliga via env var.
            if (valores.Length == 0)
            {
                config[$"{chave}:0"] = string.Empty;
                return;
            }

            for (var i = 0; i < valores.Length; i++)
                config[$"{chave}:{i}"] = valores[i];
        }

        public static WebApplication Criar(
            IDictionary<string, string?>? config = null,
            bool isDevelopment = false,
            bool comHsts = true,
            bool comRateLimiter = false,
            Action<WebApplication>? mapearRotas = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = isDevelopment ? Environments.Development : Environments.Production
            });

            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            if (config is not null)
                builder.Configuration.AddInMemoryCollection(config);

            // Caminho de produção, incluindo o binding da seção.
            builder.Services.AddTratooForwardedHeaders(builder.Configuration);

            if (comRateLimiter)
                builder.Services.AddTratooRateLimiter();

            var app = builder.Build();

            // "Camada de transporte" simulada — define o peer da conexão.
            app.Use(async (context, next) =>
            {
                var peer = context.Request.Headers[HeaderPeer].ToString();
                if (!string.IsNullOrWhiteSpace(peer) && IPAddress.TryParse(peer, out var endereco))
                    context.Connection.RemoteIpAddress = endereco;

                await next();
            });

            app.UseGateDePeerConfiavel();
            app.UseForwardedHeaders();

            if (comHsts && !isDevelopment)
                app.UseHsts();

            if (comRateLimiter)
                app.UseRateLimiter();

            // Rota padrão: devolve o que a aplicação enxerga depois do pipeline.
            app.MapGet("/eco", (HttpContext http) => Results.Ok(new EcoResposta(
                ClientRequestInfo.ObterIp(http),
                http.Request.Scheme,
                http.Request.IsHttps)));

            mapearRotas?.Invoke(app);

            return app;
        }
    }

    public record EcoResposta(string Ip, string Scheme, bool IsHttps);
}
