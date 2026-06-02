using System.Security.Claims;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class CompetenciaExtensions
    {
        public static void AddEndPointsCompetencias(this WebApplication app)
        {
            // ── GET /prestadores/{prestadorId}/competencias — público ─────────
            app.MapGet("/prestadores/{prestadorId}/competencias", async (
                int prestadorId,
                CompetenciaService service) =>
            {
                var competencias = await service.VisualizarAsync(prestadorId);
                return Results.Ok(competencias);
            });

            // ── POST /prestadores/{prestadorId}/competencias — autenticado ────
            // Requer que o JWT userId == prestadorId (o prestador só edita a si mesmo).
            app.MapPost("/prestadores/{prestadorId}/competencias", async (
                int prestadorId,
                PostCompetenciaRequest request,
                HttpContext http,
                CompetenciaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();
                if (userId.Value != prestadorId) return Results.Forbid();

                await service.AdicionarAsync(new CompetenciaDTO
                {
                    PrestadorId = prestadorId,
                    Nome        = request.Nome,
                    Nivel       = request.Nivel
                });

                return Results.Created($"/prestadores/{prestadorId}/competencias", null);
            }).RequireAuthorization();

            // ── PUT /prestadores/{prestadorId}/competencias/{id}/nivel ────────
            app.MapPut("/prestadores/{prestadorId}/competencias/{id}/nivel", async (
                int prestadorId,
                int id,
                PutCompetenciaRequest request,
                HttpContext http,
                CompetenciaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();
                if (userId.Value != prestadorId) return Results.Forbid();

                await service.EditarAsync(prestadorId, id, request.Nivel);
                return Results.NoContent();
            }).RequireAuthorization();

            // ── DELETE /prestadores/{prestadorId}/competencias/{id} ───────────
            app.MapDelete("/prestadores/{prestadorId}/competencias/{id}", async (
                int prestadorId,
                int id,
                HttpContext http,
                CompetenciaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();
                if (userId.Value != prestadorId) return Results.Forbid();

                await service.RemoverAsync(id, prestadorId);
                return Results.Ok(new { mensagem = "Competência removida com sucesso" });
            }).RequireAuthorization();
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
