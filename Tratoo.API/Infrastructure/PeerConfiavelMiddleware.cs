using Microsoft.Extensions.Primitives;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Trust boundary dos headers <c>X-Forwarded-*</c>.
    ///
    /// Roda ANTES do <c>UseForwardedHeaders()</c>. Se a conexão TCP não vier de uma faixa
    /// confiável (<see cref="ForwardedHeadersSettings.PeersConfiaveis"/>), remove os
    /// headers encaminhados da requisição — o middleware oficial then não encontra nada
    /// para aplicar e <c>RemoteIpAddress</c> permanece o IP real de quem conectou.
    ///
    /// POR QUE UM GATE PRÓPRIO, E NÃO KnownProxies/KnownNetworks:
    /// o middleware do ASP.NET Core valida cada salto da cadeia, não só o peer. Com
    /// ForwardLimit=2 e KnownNetworks cobrindo apenas a faixa do peer, ele para no
    /// primeiro salto e adota o IP do pool de borda do Railway como se fosse o cliente —
    /// exatamente o bug que a correção de ForwardLimit resolveu. Cobrir os dois saltos
    /// exigiria a faixa do pool de borda, que o Railway não documenta. Este gate separa
    /// as duas responsabilidades: aqui decide-se QUEM pode apresentar headers; o
    /// middleware oficial cuida de QUANTOS saltos percorrer.
    ///
    /// Falha de forma segura e visível: descarta o header (em vez de confiar) e loga
    /// aviso, para que uma faixa mal configurada apareça no log em vez de corromper
    /// silenciosamente a auditoria.
    /// </summary>
    public sealed class PeerConfiavelMiddleware
    {
        /// <summary>
        /// Headers removidos quando o peer não é confiável. Inclui Host e X-Real-IP
        /// mesmo sem serem processados hoje: se um dia passarem a ser lidos, já chegam
        /// limpos de origem não confiável.
        /// </summary>
        private static readonly string[] HeadersEncaminhados =
        {
            "X-Forwarded-For",
            "X-Forwarded-Proto",
            "X-Forwarded-Host",
            "X-Forwarded-Port",
            "X-Real-IP"
        };

        private readonly RequestDelegate _next;
        private readonly RedeConfiavel _peersConfiaveis;
        private readonly ILogger<PeerConfiavelMiddleware> _logger;

        public PeerConfiavelMiddleware(
            RequestDelegate next,
            RedeConfiavel peersConfiaveis,
            ILogger<PeerConfiavelMiddleware> logger)
        {
            _next = next;
            _peersConfiaveis = peersConfiaveis;
            _logger = logger;
        }

        public Task InvokeAsync(HttpContext context)
        {
            // Gate desligado por configuração (lista vazia) — opt-out explícito.
            if (_peersConfiaveis.Vazio)
                return _next(context);

            var peer = context.Connection.RemoteIpAddress;

            if (!_peersConfiaveis.Contem(peer))
            {
                var tinhaHeaders = RemoverHeadersEncaminhados(context);

                if (tinhaHeaders)
                {
                    _logger.LogWarning(
                        "X-Forwarded-* descartados: peer {Peer} fora das faixas confiáveis ({Faixas}). " +
                        "Se isto aparecer para tráfego legítimo, a lista PeersConfiaveis está desalinhada " +
                        "com a topologia real.",
                        peer?.ToString() ?? "desconhecido",
                        string.Join(", ", _peersConfiaveis.FaixasConfiguradas));
                }
            }

            return _next(context);
        }

        private static bool RemoverHeadersEncaminhados(HttpContext context)
        {
            var removeuAlgum = false;

            foreach (var header in HeadersEncaminhados)
            {
                if (context.Request.Headers.TryGetValue(header, out StringValues valor)
                    && !StringValues.IsNullOrEmpty(valor))
                {
                    context.Request.Headers.Remove(header);
                    removeuAlgum = true;
                }
            }

            return removeuAlgum;
        }
    }

    public static class PeerConfiavelMiddlewareExtensions
    {
        /// <summary>
        /// Registra o gate. Precisa vir imediatamente antes de <c>UseForwardedHeaders()</c>.
        /// </summary>
        public static IApplicationBuilder UseGateDePeerConfiavel(this WebApplication app)
        {
            var rede = app.Services.GetRequiredService<RedeConfiavel>();

            if (rede.FaixasInvalidas.Count > 0)
            {
                app.Logger.LogError(
                    "ForwardedHeaders:PeersConfiaveis tem entradas inválidas e ignoradas: {Invalidas}",
                    string.Join(", ", rede.FaixasInvalidas));
            }

            if (rede.Vazio)
            {
                app.Logger.LogWarning(
                    "ForwardedHeaders:PeersConfiaveis está vazio — X-Forwarded-* será aceito de " +
                    "QUALQUER peer. Só é seguro se a aplicação não estiver atrás de proxy.");
            }
            else
            {
                app.Logger.LogInformation(
                    "Trust boundary de X-Forwarded-*: {Faixas}",
                    string.Join(", ", rede.FaixasConfiguradas));
            }

            return app.UseMiddleware<PeerConfiavelMiddleware>();
        }
    }
}
