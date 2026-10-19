using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Roda o Tratoo.API de verdade (<c>dotnet Tratoo.API.dll</c>), num processo à parte, para
    /// testar o que só existe no processo real: os modos de linha de comando e as recusas de
    /// subida. Nada de configuração da máquina vaza para dentro dele:
    ///  • diretório de trabalho VAZIO — o appsettings.json copiado para a pasta dos testes
    ///    aponta para o banco de produção e não pode ser lido;
    ///  • connection strings e a chave de dados herdadas do ambiente são removidas; vale só o
    ///    que o teste passar.
    /// </summary>
    internal static class ProcessoApi
    {
        public static int PortaLivre()
        {
            using var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }

        /// <param name="argumento">Ex.: <c>--migrate-only</c>; <c>null</c> = subida normal da API.</param>
        public static async Task<(int ExitCode, string Saida)> RodarAsync(
            string? argumento, IDictionary<string, string?> ambiente, int limiteSegundos = 90)
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "Tratoo.API.dll");
            Assert.True(File.Exists(dll), $"Tratoo.API.dll não encontrado em {AppContext.BaseDirectory}");

            var cwd = Directory.CreateTempSubdirectory("tratoo-processo-teste-").FullName;

            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(dll);
            if (argumento is not null)
                psi.ArgumentList.Add(argumento);

            foreach (var chave in psi.Environment.Keys
                         .Where(k => k.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)
                                  || k.StartsWith("DataProtector__", StringComparison.OrdinalIgnoreCase))
                         .ToList())
                psi.Environment.Remove(chave);
            foreach (var (k, v) in ambiente)
                psi.Environment[k] = v;

            using var processo = Process.Start(psi)!;
            var stdout = processo.StandardOutput.ReadToEndAsync();
            var stderr = processo.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(limiteSegundos));
            try
            {
                await processo.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                processo.Kill(entireProcessTree: true);
                throw new TimeoutException($"O processo ({argumento ?? "subida normal"}) não encerrou em {limiteSegundos}s.");
            }
            finally
            {
                try { Directory.Delete(cwd, recursive: true); } catch { /* arquivo ainda em uso: pasta temporária */ }
            }

            return (processo.ExitCode, await stdout + await stderr);
        }
    }
}
