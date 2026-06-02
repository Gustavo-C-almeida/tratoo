using System.Security.Claims;

namespace Tratoo.API.EndPoints
{
    public static class CompetenciaRelacionamentoExtensions
    {
        public static void AddEndPointsCompetenciaRelacionamento(this WebApplication app)
        {
            // ── EXPERIÊNCIAS ──────────────────────────────────────────────────

            // GET  /prestadores/{id}/experiencias/{expId}/competencias
            app.MapGet("/prestadores/{id}/experiencias/{expId}/competencias", async (
                int id, int expId,
                CompetenciaRelacionamentoService service) =>
            {
                var lista = await service.ListarPorExperienciaAsync(id, expId);
                return Results.Ok(lista);
            });

            // POST /prestadores/me/experiencias/{expId}/competencias/{compId}
            app.MapPost("/prestadores/me/experiencias/{expId}/competencias/{compId}", async (
                int expId, int compId,
                HttpContext http,
                CompetenciaRelacionamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.AssociarCompetenciaExperienciaAsync(userId.Value, compId, expId);
                return Results.Created($"/prestadores/{userId.Value}/experiencias/{expId}/competencias", null);
            }).RequireAuthorization();

            // DELETE /prestadores/me/experiencias/{expId}/competencias/{compId}
            app.MapDelete("/prestadores/me/experiencias/{expId}/competencias/{compId}", async (
                int expId, int compId,
                HttpContext http,
                CompetenciaRelacionamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.DesassociarCompetenciaExperienciaAsync(userId.Value, compId, expId);
                return Results.Ok(new { mensagem = "Competência desvinculada da experiência" });
            }).RequireAuthorization();

            // ── CERTIFICAÇÕES ─────────────────────────────────────────────────

            // GET  /prestadores/{id}/certificacoes/{certId}/competencias
            app.MapGet("/prestadores/{id}/certificacoes/{certId}/competencias", async (
                int id, int certId,
                CompetenciaRelacionamentoService service) =>
            {
                var lista = await service.ListarPorCertificacaoAsync(id, certId);
                return Results.Ok(lista);
            });

            // POST /prestadores/me/certificacoes/{certId}/competencias/{compId}
            app.MapPost("/prestadores/me/certificacoes/{certId}/competencias/{compId}", async (
                int certId, int compId,
                HttpContext http,
                CompetenciaRelacionamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.AssociarCompetenciaCertificacaoAsync(userId.Value, compId, certId);
                return Results.Created($"/prestadores/{userId.Value}/certificacoes/{certId}/competencias", null);
            }).RequireAuthorization();

            // DELETE /prestadores/me/certificacoes/{certId}/competencias/{compId}
            app.MapDelete("/prestadores/me/certificacoes/{certId}/competencias/{compId}", async (
                int certId, int compId,
                HttpContext http,
                CompetenciaRelacionamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.DesassociarCompetenciaCertificacaoAsync(userId.Value, compId, certId);
                return Results.Ok(new { mensagem = "Competência desvinculada da certificação" });
            }).RequireAuthorization();

            // ── PORTFÓLIO ─────────────────────────────────────────────────────

            // GET  /prestadores/{id}/portfolio/{portId}/competencias
            app.MapGet("/prestadores/{id}/portfolio/{portId}/competencias", async (
                int id, int portId,
                CompetenciaRelacionamentoService service) =>
            {
                var lista = await service.ListarPorPortfolioAsync(id, portId);
                return Results.Ok(lista);
            });

            // POST /prestadores/me/portfolio/{portId}/competencias/{compId}
            app.MapPost("/prestadores/me/portfolio/{portId}/competencias/{compId}", async (
                int portId, int compId,
                HttpContext http,
                CompetenciaRelacionamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.AssociarCompetenciaPortfolioAsync(userId.Value, compId, portId);
                return Results.Created($"/prestadores/{userId.Value}/portfolio/{portId}/competencias", null);
            }).RequireAuthorization();

            // DELETE /prestadores/me/portfolio/{portId}/competencias/{compId}
            app.MapDelete("/prestadores/me/portfolio/{portId}/competencias/{compId}", async (
                int portId, int compId,
                HttpContext http,
                CompetenciaRelacionamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.DesassociarCompetenciaPortfolioAsync(userId.Value, compId, portId);
                return Results.Ok(new { mensagem = "Competência desvinculada do item de portfólio" });
            }).RequireAuthorization();

            // ── RELACIONAMENTOS de uma competência ────────────────────────────

            // GET /prestadores/{id}/competencias/{compId}/relacionamentos
            app.MapGet("/prestadores/{id}/competencias/{compId}/relacionamentos", async (
                int id, int compId,
                CompetenciaRelacionamentoService service) =>
            {
                var dto = await service.VisualizarRelacionamentosAsync(id, compId);
                return Results.Ok(dto);
            });
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
