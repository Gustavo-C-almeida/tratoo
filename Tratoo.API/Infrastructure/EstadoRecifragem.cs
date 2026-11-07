using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tratoo.API.BackgroundServices;

namespace Tratoo.API.Infrastructure
{
    /// <summary>Resultado da última rodada de recifragem automática nesta réplica.</summary>
    /// <param name="Pendentes">Valores ainda na chave anterior ou em AES-CBC, relidos depois da rodada (-1 se falhou).</param>
    /// <param name="Ilegiveis">Valores que nenhuma chave configurada decifra, ou ambíguos (-1 se falhou).</param>
    /// <param name="Erro">Tipo da exceção, se a rodada falhou (e foi desfeita).</param>
    public sealed record ResultadoRecifragem(
        DateTime Em, int Pendentes, int Ilegiveis, int Recifrados, bool ComChaveAnterior, string? Erro)
    {
        public bool Concluida => Erro is null && Pendentes == 0 && Ilegiveis == 0;
    }

    /// <summary>Singleton: a última rodada que ESTA réplica executou.</summary>
    public sealed class EstadoRecifragem
    {
        private volatile ResultadoRecifragem? _ultima;

        public ResultadoRecifragem? Ultima => _ultima;

        public void Registrar(ResultadoRecifragem resultado) => _ultima = resultado;
    }

    /// <summary>
    /// Check "recifragem" do /health/ready. Nunca Unhealthy: a recifragem não decide se a
    /// réplica recebe tráfego. Degraded (HTTP 200) quando precisa de atenção — é o que o
    /// workflow de monitoramento procura.
    /// </summary>
    public sealed class RecifragemHealthCheck : IHealthCheck
    {
        private readonly EstadoRecifragem _estado;
        private readonly IConfiguration _configuration;

        public RecifragemHealthCheck(EstadoRecifragem estado, IConfiguration configuration)
        {
            _estado = estado;
            _configuration = configuration;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
            Task.FromResult(Avaliar(RecifragemAutomaticaService.EstaLigado(_configuration), _estado.Ultima));

        public static HealthCheckResult Avaliar(bool ligado, ResultadoRecifragem? ultima)
        {
            if (!ligado)
                return HealthCheckResult.Healthy("Recifragem automática desligada.");

            if (ultima is null)
                return HealthCheckResult.Healthy("Recifragem automática ligada; nenhuma rodada nesta réplica ainda.");

            if (ultima.Erro is not null)
                return HealthCheckResult.Degraded(
                    $"Última rodada falhou ({ultima.Erro}) e foi desfeita; nova tentativa na próxima rodada. " +
                    "NÃO remova DataProtector__ChaveAnterior.");

            if (ultima.Ilegiveis > 0)
                return HealthCheckResult.Degraded(
                    $"{ultima.Ilegiveis} valor(es) não decifram com nenhuma chave configurada. " +
                    "NÃO remova DataProtector__ChaveAnterior; investigue antes.");

            if (ultima.Pendentes > 0)
                return HealthCheckResult.Degraded(
                    $"{ultima.Pendentes} valor(es) ainda pendentes depois da rodada. NÃO remova DataProtector__ChaveAnterior.");

            return HealthCheckResult.Healthy(ultima.ComChaveAnterior
                ? $"Concluída em {ultima.Em:yyyy-MM-dd HH:mm} UTC: tudo em v2 na chave atual. " +
                  "DataProtector__ChaveAnterior já pode ser removida."
                : $"Concluída em {ultima.Em:yyyy-MM-dd HH:mm} UTC: tudo em v2 na chave atual.");
        }
    }
}
