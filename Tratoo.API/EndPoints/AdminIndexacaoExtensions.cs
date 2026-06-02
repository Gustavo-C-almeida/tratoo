using Tratoo.API.BackgroundServices;

namespace Tratoo.API.EndPoints
{
    public static class AdminIndexacaoExtensions
    {
        public static void AddEndPointsAdminIndexacao(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // GET /api/admin/indexar/status
            // Estatísticas de indexação: total indexado, sem embedding, última execução.
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/admin/indexar/status", async (
                BuscaSemanticaService service) =>
            {
                var status = await service.ObterStatusAsync();
                return Results.Ok(status);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // POST /api/admin/indexar/prestador/{id}
            // Reindexar um prestador específico manualmente.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/admin/indexar/prestador/{id:int}", async (
                int id,
                PrestadorIndexadorService indexador) =>
            {
                await indexador.IndexarAsync(id);
                return Results.Ok(new { mensagem = $"Prestador {id} reindexado." });
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // POST /api/admin/indexar/projeto/{id}
            // Reindexar um projeto específico manualmente.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/admin/indexar/projeto/{id:int}", async (
                int id,
                ProjetoIndexadorService indexador) =>
            {
                await indexador.IndexarAsync(id);
                return Results.Ok(new { mensagem = $"Projeto {id} reindexado." });
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // POST /api/admin/indexar/batch
            // Dispara reindexação manual de todos os prestadores e projetos sem embedding
            // ou com embedding desatualizado (> 7 dias). Roda em background.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/admin/indexar/batch", (
                ReindexacaoBackgroundService.IBatchReindexador reindexador) =>
            {
                _ = Task.Run(() => reindexador.ExecutarBatchAsync(CancellationToken.None));
                return Results.Accepted(null, new { mensagem = "Reindexação em batch iniciada em background." });
            }).RequireAuthorization();
        }
    }
}
