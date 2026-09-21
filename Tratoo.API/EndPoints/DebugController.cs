namespace Tratoo.API.EndPoints
{
    /// <summary>
    /// Endpoint de debug temporário para inspecionar, em uma requisição real,
    /// qual IP chega ao container por trás do proxy de borda da Railway.
    ///
    /// Implementado como extension method (Minimal API) para seguir o padrão
    /// já usado no restante do projeto (ver <c>EndPoints/*Extensions.cs</c>) —
    /// não há Controllers/MVC registrado em <c>Program.cs</c>.
    ///
    /// Propositalmente sem autenticação: o objetivo é conseguir chamar a rota
    /// de fora (e ver o log de deploy) sem depender de login. Não expõe nenhum
    /// dado sensível — apenas metadados de conexão/headers da própria requisição.
    ///
    /// Não altera o pipeline de middleware (UseForwardedHeaders já está
    /// registrado antes disso em Program.cs) — este endpoint só lê e loga o
    /// estado da conexão no momento em que chega ao handler.
    /// </summary>
    public static class DebugController
    {
        public static void AddEndPointsDebug(this WebApplication app)
        {
            app.MapGet("/api/debug/connection-info", (HttpContext http, ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("DebugController");

                var connection = http.Connection;
                var request = http.Request;

                var info = new
                {
                    remoteIpAddress = connection.RemoteIpAddress?.ToString(),
                    remotePort = connection.RemotePort,
                    localIpAddress = connection.LocalIpAddress?.ToString(),
                    localPort = connection.LocalPort,
                    headers = new
                    {
                        xForwardedFor = request.Headers["X-Forwarded-For"].ToString(),
                        xForwardedProto = request.Headers["X-Forwarded-Proto"].ToString(),
                        xForwardedHost = request.Headers["X-Forwarded-Host"].ToString(),
                        xForwardedPort = request.Headers["X-Forwarded-Port"].ToString(),
                        host = request.Headers["Host"].ToString()
                    },
                    requestUtc = DateTime.UtcNow,
                    requestLocal = DateTimeOffset.Now
                };

                logger.LogInformation(
                    "Debug connection-info :: RemoteIp={RemoteIpAddress} RemotePort={RemotePort} " +
                    "LocalIp={LocalIpAddress} LocalPort={LocalPort} " +
                    "X-Forwarded-For={XForwardedFor} X-Forwarded-Proto={XForwardedProto} " +
                    "X-Forwarded-Host={XForwardedHost} X-Forwarded-Port={XForwardedPort} " +
                    "Host={Host} RequestUtc={RequestUtc}",
                    info.remoteIpAddress, info.remotePort,
                    info.localIpAddress, info.localPort,
                    info.headers.xForwardedFor, info.headers.xForwardedProto,
                    info.headers.xForwardedHost, info.headers.xForwardedPort,
                    info.headers.host, info.requestUtc);

                return Results.Ok(info);
            })
            .AllowAnonymous();
        }
    }
}
