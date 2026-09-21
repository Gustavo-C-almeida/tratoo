using Tratoo.API.Infrastructure;

namespace Tratoo.API.EndPoints
{
    /// <summary>
    /// Endpoint temporário de diagnóstico para descobrir se o IP do proxy da
    /// plataforma (visto em Connection.RemoteIpAddress ANTES de qualquer
    /// processamento de X-Forwarded-For) é estável ou muda com o tempo.
    ///
    /// Simples e append-only de propósito: cada chamada grava uma linha em
    /// /app/logs/proxy-ip-tests.txt e devolve as últimas 100 linhas já
    /// resumidas. Sem lógica complexa, sem banco de dados — é só para
    /// observação manual repetida ao longo de horas/dias.
    /// </summary>
    public static class DebugProxyIpExtensions
    {
        private const string CaminhoArquivo = "logs/proxy-ip-tests.txt";
        private static readonly SemaphoreSlim Trava = new(1, 1);

        public static void AddEndPointsDebugProxyIp(this WebApplication app)
        {
            var rota = app.MapGet("/api/debug/test-proxy-ip", async (HttpContext http) =>
            {
                // Importante: lido diretamente da conexão, SEM passar pelo
                // ClientRequestInfo/UseForwardedHeaders — queremos o peer TCP
                // real (o proxy), não o IP original do cliente.
                var remoteIp   = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                var remotePort = http.Connection.RemotePort;
                var timestamp  = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

                var linha = $"{timestamp} | {remoteIp} | {remotePort}";

                Directory.CreateDirectory(Path.GetDirectoryName(CaminhoArquivo)!);

                await Trava.WaitAsync();
                try
                {
                    await File.AppendAllTextAsync(CaminhoArquivo, linha + Environment.NewLine);
                }
                finally
                {
                    Trava.Release();
                }

                var todasLinhas = File.Exists(CaminhoArquivo)
                    ? await File.ReadAllLinesAsync(CaminhoArquivo)
                    : Array.Empty<string>();

                var ultimas100 = todasLinhas
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .TakeLast(100)
                    .ToArray();

                var registros = ultimas100
                    .Select(l =>
                    {
                        var partes = l.Split(" | ");
                        return new
                        {
                            timestamp   = partes.ElementAtOrDefault(0) ?? "",
                            remote_ip   = partes.ElementAtOrDefault(1) ?? "",
                            remote_port = partes.ElementAtOrDefault(2) ?? ""
                        };
                    })
                    .ToArray();

                var ipFrequency = registros
                    .GroupBy(r => r.remote_ip)
                    .ToDictionary(g => g.Key, g => g.Count());

                return Results.Ok(new
                {
                    registros,
                    unique_ips_seen = ipFrequency.Count,
                    ip_frequency    = ipFrequency
                });
            });

            if (!app.Environment.IsDevelopment())
                rota.RequireAuthorization("Admin");
        }
    }
}
