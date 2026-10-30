using System.Diagnostics;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Acesso mínimo à CLI do Docker para os testes de integração (Postgres e Redis
    /// reais). Sem pacote novo: os contêineres sobem com <c>docker run</c> numa porta
    /// aleatória de 127.0.0.1 e são removidos no fim (<c>docker rm -f</c>). Todos levam o
    /// rótulo <c>tratoo-testes=1</c> — se um processo de teste morrer no meio, limpe com
    /// <c>docker rm -f $(docker ps -aq --filter label=tratoo-testes=1)</c>.
    /// </summary>
    internal static class DockerCli
    {
        private static readonly Lazy<bool> _disponivel = new(() =>
        {
            try { return Executar(TimeSpan.FromSeconds(60), "info", "--format", "{{.ServerVersion}}").ExitCode == 0; }
            catch { return false; }
        });

        public static bool Disponivel => _disponivel.Value;

        /// <summary>
        /// <c>TRATOO_EXIGIR_DOCKER=1</c> (definido no CI): sem Docker, os testes de integração
        /// FALHAM em vez de serem ignorados. Sem isso, um runner com Docker quebrado deixaria
        /// o pipeline verde sem ter rodado nenhum teste contra Postgres/Redis reais.
        /// </summary>
        public static bool Exigido => Environment.GetEnvironmentVariable("TRATOO_EXIGIR_DOCKER") == "1";

        /// <summary>Chamado pelas fixtures quando o Docker não está disponível.</summary>
        public static void FalharSeExigido()
        {
            if (Exigido)
                throw new InvalidOperationException(
                    "TRATOO_EXIGIR_DOCKER=1, mas o Docker não respondeu a `docker info`. " +
                    "Os testes de integração precisam de Postgres/Redis reais e não podem ser ignorados aqui.");
        }

        public static (int ExitCode, string Saida, string Erro) Executar(TimeSpan timeout, params string[] args)
        {
            var psi = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)!;
            var saida = p.StandardOutput.ReadToEndAsync();
            var erro = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                p.Kill(entireProcessTree: true);
                throw new TimeoutException($"docker {string.Join(' ', args)} não terminou em {timeout}.");
            }

            return (p.ExitCode, saida.Result, erro.Result);
        }
    }

    /// <summary>
    /// [Fact] que vira "Ignorado" (com o motivo) quando não há Docker — nunca um falso
    /// "Aprovado". Com Docker disponível, roda normalmente. Com <see cref="DockerCli.Exigido"/>,
    /// nunca é ignorado: o teste roda e a fixture falha com a explicação.
    /// </summary>
    public sealed class FactComDockerAttribute : FactAttribute
    {
        public FactComDockerAttribute()
        {
            if (!DockerCli.Disponivel && !DockerCli.Exigido)
                Skip = "Docker indisponível: teste de integração com Postgres/Redis reais. Suba o Docker para executá-lo.";
        }
    }

    internal sealed class ContainerDocker : IAsyncDisposable
    {
        public string Id { get; }
        public int Porta { get; }

        private ContainerDocker(string id, int porta)
        {
            Id = id;
            Porta = porta;
        }

        public static ContainerDocker Iniciar(
            string imagem, int portaInterna,
            IEnumerable<string>? ambiente = null, IEnumerable<string>? comando = null)
        {
            var args = new List<string>
            {
                "run", "-d", "--rm", "--label", "tratoo-testes=1",
                "-p", $"127.0.0.1::{portaInterna}"
            };
            foreach (var e in ambiente ?? Array.Empty<string>())
            {
                args.Add("-e");
                args.Add(e);
            }
            args.Add(imagem);
            args.AddRange(comando ?? Array.Empty<string>());

            // Timeout folgado: na primeira vez o Docker pode precisar baixar a imagem.
            var run = DockerCli.Executar(TimeSpan.FromMinutes(5), args.ToArray());
            if (run.ExitCode != 0)
                throw new InvalidOperationException($"docker run {imagem} falhou: {run.Erro}");

            var id = run.Saida.Trim();

            var porta = DockerCli.Executar(TimeSpan.FromSeconds(30), "port", id, $"{portaInterna}/tcp");
            var linha = porta.Saida
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(l => l.Contains("127.0.0.1"));

            return new ContainerDocker(id, int.Parse(linha.Trim().Split(':').Last()));
        }

        /// <summary>Para o contêiner sem removê-lo do registro (simula o serviço fora do ar).</summary>
        public void Parar() => DockerCli.Executar(TimeSpan.FromSeconds(60), "stop", "-t", "1", Id);

        public ValueTask DisposeAsync()
        {
            DockerCli.Executar(TimeSpan.FromSeconds(60), "rm", "-f", Id);
            return ValueTask.CompletedTask;
        }
    }
}
