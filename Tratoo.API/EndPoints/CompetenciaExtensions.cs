using System.Security.Claims;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class CompetenciaExtensions
    {
        public static void AddEndPointsCompetencias(this WebApplication app)
        {
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
