using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.API.BackgroundServices
{
    /// <summary>
    /// Roda toda segunda-feira às 02:00 UTC para garantir que todos os prestadores e projetos
    /// estejam indexados — inclusive os que falharam silenciosamente durante o evento de origem.
    /// </summary>
    public class ReindexacaoBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReindexacaoBackgroundService> _logger;

        public ReindexacaoBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<ReindexacaoBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReindexacaoBackgroundService iniciado.");

            // Aguarda startup da aplicação
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = CalcularDelayAteProximaSegunda();
                _logger.LogInformation(
                    "Próxima reindexação em batch em {Horas:F1} horas.", delay.TotalHours);

                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var batch = scope.ServiceProvider.GetRequiredService<IBatchReindexador>();
                    await batch.ExecutarBatchAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Erro durante reindexação batch.");
                }
            }
        }

        private static TimeSpan CalcularDelayAteProximaSegunda()
        {
            var agora = DateTime.UtcNow;
            // Próxima segunda-feira às 02:00 UTC
            int diasAteSegunda = ((int)DayOfWeek.Monday - (int)agora.DayOfWeek + 7) % 7;
            if (diasAteSegunda == 0 && agora.Hour >= 2) diasAteSegunda = 7;

            var proxima = agora.Date
                .AddDays(diasAteSegunda)
                .AddHours(2);

            var delay = proxima - agora;
            return delay > TimeSpan.Zero ? delay : TimeSpan.FromDays(7);
        }

        // ─── Interface exposta para o endpoint /batch ─────────────────────────
        public interface IBatchReindexador
        {
            Task ExecutarBatchAsync(CancellationToken ct);
        }

        // ─── Implementação que pode ser injetada diretamente ──────────────────
        public class BatchReindexador : IBatchReindexador
        {
            private readonly TratooContext _db;
            private readonly VectorContext _vectorDb;
            private readonly PrestadorIndexadorService _prestadorIndexador;
            private readonly ProjetoIndexadorService _projetoIndexador;
            private readonly ILogger<BatchReindexador> _logger;

            public BatchReindexador(
                TratooContext db,
                VectorContext vectorDb,
                PrestadorIndexadorService prestadorIndexador,
                ProjetoIndexadorService projetoIndexador,
                ILogger<BatchReindexador> logger)
            {
                _db = db;
                _vectorDb = vectorDb;
                _prestadorIndexador = prestadorIndexador;
                _projetoIndexador = projetoIndexador;
                _logger = logger;
            }

            public async Task ExecutarBatchAsync(CancellationToken ct)
            {
                _logger.LogInformation("Iniciando reindexação batch em {Hora}.", DateTime.UtcNow);

                await ReindexarPrestadoresAsync(ct);
                await ReindexarProjetosAsync(ct);

                _logger.LogInformation("Reindexação batch concluída.");
            }

            private async Task ReindexarPrestadoresAsync(CancellationToken ct)
            {
                // SQL Server: todos os prestadores ativos
                var todosAtivos = await _db.Prestadores
                    .Where(u => u.Status == StatusUsuario.Active)
                    .Select(u => u.Id)
                    .ToListAsync(ct);

                // PostgreSQL: embeddings recentes (< 7 dias)
                var limiteReindexacao = DateTime.UtcNow.AddDays(-7);
                var recentes = await _vectorDb.PrestadorEmbeddings
                    .Where(e => e.IndexadoEm > limiteReindexacao)
                    .Select(e => e.PrestadorId)
                    .ToListAsync(ct);

                var recentesSet = recentes.ToHashSet();
                var paraIndexar = todosAtivos.Where(id => !recentesSet.Contains(id)).ToList();

                _logger.LogInformation("{Count} prestadores para reindexar.", paraIndexar.Count);

                // Sequencial: PrestadorIndexadorService compartilha um único DbContext
                // (scoped), que não é thread-safe. Processar em paralelo lançaria
                // "second operation started on this context" — engolido pelo catch
                // por item, fazendo a reindexação falhar em silêncio.
                foreach (var chunk in paraIndexar.Chunk(10))
                {
                    if (ct.IsCancellationRequested) break;
                    foreach (var id in chunk)
                        await _prestadorIndexador.IndexarAsync(id);
                    await Task.Delay(500, ct);
                }
            }

            private async Task ReindexarProjetosAsync(CancellationToken ct)
            {
                // SQL Server: projetos publicados e abertos
                var todosAbertos = await _db.Projetos
                    .Where(p => p.Publicado && p.Status == StatusProjeto.Aberto)
                    .Select(p => p.Id)
                    .ToListAsync(ct);

                // PostgreSQL: embeddings recentes (< 7 dias)
                var limiteReindexacao = DateTime.UtcNow.AddDays(-7);
                var recentes = await _vectorDb.ProjetoEmbeddings
                    .Where(e => e.IndexadoEm > limiteReindexacao)
                    .Select(e => e.ProjetoId)
                    .ToListAsync(ct);

                var recentesSet = recentes.ToHashSet();
                var paraIndexar = todosAbertos.Where(id => !recentesSet.Contains(id)).ToList();

                _logger.LogInformation("{Count} projetos para reindexar.", paraIndexar.Count);

                // Sequencial pelo mesmo motivo de ReindexarPrestadoresAsync:
                // o DbContext scoped compartilhado não é thread-safe.
                foreach (var chunk in paraIndexar.Chunk(10))
                {
                    if (ct.IsCancellationRequested) break;
                    foreach (var id in chunk)
                        await _projetoIndexador.IndexarAsync(id);
                    await Task.Delay(500, ct);
                }
            }
        }
    }
}
