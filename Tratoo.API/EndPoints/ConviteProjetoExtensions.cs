using System.Security.Claims;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class ConviteProjetoExtensions
    {
        public static void AddEndPointsConviteProjeto(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // ENVIAR CONVITE — contratante convida prestador para projeto
            // POST /api/projects/{projetoId}/convites — somente Contratante
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/projects/{projetoId:int}/convites", async (
                int projetoId,
                CriarConviteProjetoRequest request,
                HttpContext http,
                ConviteProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dto = new CriarConviteProjetoDTO
                {
                    ProjetoId = projetoId,
                    ContratanteId = userId.Value,
                    PrestadorId = request.PrestadorId,
                    MensagemInicial = request.MensagemInicial,
                    OrcamentoSugerido = request.OrcamentoSugerido,
                    PrazoDesejado = request.PrazoDesejado
                };

                var convite = await service.EnviarAsync(dto);
                return Results.Created($"/api/convites/{convite.Id}", convite);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // LISTAR CONVITES DO PROJETO — contratante vê convites enviados
            // GET /api/projects/{projetoId}/convites — somente Contratante
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/projects/{projetoId:int}/convites", async (
                int projetoId,
                HttpContext http,
                ConviteProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var convites = await service.ListarDoProjetoAsync(projetoId, userId.Value);
                return Results.Ok(convites);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // ACEITAR CONVITE — prestador aceita convite
            // PUT /api/convites/{id}/aceitar — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapPut("/api/convites/{id:guid}/aceitar", async (
                Guid id,
                HttpContext http,
                ConviteProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var convite = await service.AceitarAsync(id, userId.Value);
                return Results.Ok(convite);
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // RECUSAR CONVITE — prestador recusa convite
            // PUT /api/convites/{id}/recusar — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapPut("/api/convites/{id:guid}/recusar", async (
                Guid id,
                RecusarConviteRequest request,
                HttpContext http,
                ConviteProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var convite = await service.RecusarAsync(id, userId.Value, request.Motivo);
                return Results.Ok(convite);
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // MEUS CONVITES — prestador vê convites recebidos
            // GET /api/me/convites — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/me/convites", async (
                HttpContext http,
                ConviteProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var convites = await service.ListarDoPrestadorAsync(userId.Value);
                return Results.Ok(convites);
            }).RequireAuthorization("Prestador");

        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
