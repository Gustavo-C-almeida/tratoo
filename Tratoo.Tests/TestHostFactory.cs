using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using Tratoo.API.Infrastructure;

namespace Tratoo.Tests
{
    /// <summary>
    /// Sobe um host in-memory com o MESMO pipeline de produção na parte que importa:
    /// UseForwardedHeaders() como primeiro middleware, UseHsts() logo em seguida e
    /// o rate limiter configurado por <see cref="RateLimiterSetup"/>.
    ///
    /// O TestServer não tem conexão TCP real, então o IP do "peer" (o proxy, do
    /// ponto de vista do Kestrel) é injetado pelo header <see cref="HeaderPeer"/>
    /// num middleware que roda ANTES do UseForwardedHeaders — exatamente a posição
    /// que o sistema operacional ocuparia num servidor real.
    /// </summary>
    public static class TestHostFactory
    {
        /// <summary>Header só de teste: simula o IP da conexão TCP recebida pelo Kestrel.</summary>
        public const string HeaderPeer = "X-Test-Peer";

        public static WebApplication Criar(
            ForwardedHeadersSettings? forwarded = null,
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

            var settings = forwarded ?? new ForwardedHeadersSettings();
            builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
                ForwardedHeadersSetup.Aplicar(options, settings, isDevelopment));

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

        /// <summary>Sobe o host e devolve um HttpClient ligado ao TestServer.</summary>
        public static async Task<(WebApplication App, HttpClient Client)> IniciarAsync(
            ForwardedHeadersSettings? forwarded = null,
            bool isDevelopment = false,
            bool comHsts = true,
            bool comRateLimiter = false,
            Action<WebApplication>? mapearRotas = null)
        {
            var app = Criar(forwarded, isDevelopment, comHsts, comRateLimiter, mapearRotas);
            await app.StartAsync();
            return (app, app.GetTestClient());
        }
    }

    public record EcoResposta(string Ip, string Scheme, bool IsHttps);
}
