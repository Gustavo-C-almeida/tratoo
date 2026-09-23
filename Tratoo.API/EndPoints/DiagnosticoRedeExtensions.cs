using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Tratoo.API.Infrastructure;

namespace Tratoo.API.EndPoints
{
    /// <summary>
    /// Endpoint de diagnóstico do reverse proxy. Existe porque o comportamento do
    /// proxy da plataforma (quantos saltos, se sobrescreve X-Forwarded-For) não é
    /// verificável a partir do código — só observando uma requisição real.
    ///
    /// Em Development é anônimo; fora de Development exige a role "Admin".
    /// </summary>
    public static class DiagnosticoRedeExtensions
    {
        public static void AddEndPointsDiagnosticoRede(this WebApplication app)
        {
            var rota = app.MapGet("/api/diagnostico/rede", (
                HttpContext http,
                IOptions<ForwardedHeadersOptions> forwardedOptions,
                RedeConfiavel peersConfiaveis) =>
            {
                var opcoes = forwardedOptions.Value;

                return Results.Ok(new
                {
                    // O que a aplicação enxerga DEPOIS do gate + UseForwardedHeaders()
                    ipDoCliente = ClientRequestInfo.ObterIp(http),
                    scheme      = http.Request.Scheme,
                    isHttps     = http.Request.IsHttps,
                    host        = http.Request.Host.Value,

                    // O que sobrou dos headers. O middleware CONSOME as entradas que
                    // aplicou e move o restante para X-Original-For / X-Original-Proto.
                    // Se o gate tiver descartado, todos vêm vazios.
                    headersRecebidos = new
                    {
                        xForwardedFor   = http.Request.Headers["X-Forwarded-For"].ToString(),
                        xForwardedProto = http.Request.Headers["X-Forwarded-Proto"].ToString(),
                        xForwardedHost  = http.Request.Headers["X-Forwarded-Host"].ToString(),
                        xRealIp         = http.Request.Headers["X-Real-IP"].ToString(),
                        xOriginalFor    = http.Request.Headers["X-Original-For"].ToString(),
                        xOriginalProto  = http.Request.Headers["X-Original-Proto"].ToString()
                    },

                    trustBoundary = new
                    {
                        // Faixas de onde a conexão TCP pode chegar. Vazio = gate desligado.
                        peersConfiaveis = peersConfiaveis.FaixasConfiguradas,
                        gateAtivo       = !peersConfiaveis.Vazio,
                        faixasInvalidas = peersConfiaveis.FaixasInvalidas
                    },

                    configuracaoAtiva = new
                    {
                        forwardedHeaders   = opcoes.ForwardedHeaders.ToString(),
                        forwardLimit       = opcoes.ForwardLimit,
                        knownProxies       = opcoes.KnownProxies.Select(p => p.ToString()).ToArray(),
                        knownNetworksCount = opcoes.KnownNetworks.Count
                    }
                });
            });

            if (!app.Environment.IsDevelopment())
                rota.RequireAuthorization("Admin");
        }
    }
}
