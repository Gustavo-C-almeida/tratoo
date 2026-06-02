using System.Security.Claims;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class ContratoExtensions
    {
        public static void AddEndPointsContrato(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // DETALHE — qualquer parte do contrato
            // GET /api/contratos/{id}
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/contratos/{id:guid}", async (
                Guid id,
                HttpContext http,
                IContratoServicoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dto = await service.ObterDetalheAsync(id, userId.Value);
                return dto == null ? Results.NotFound() : Results.Ok(dto);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // LISTAR — contratos do usuário atual
            // GET /api/me/contratos
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/me/contratos", async (
                HttpContext http,
                IContratoServicoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var tipo = ExtrairTipoUsuario(http);
                var contratos = tipo == "Prestador"
                    ? await service.ListarDoPrestadorAsync(userId.Value)
                    : await service.ListarDoContratanteAsync(userId.Value);

                return Results.Ok(contratos);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // ASSINAR — assinatura digital do contrato
            // POST /api/contratos/{id}/assinar
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/contratos/{id:guid}/assinar", async (
                Guid id,
                AssinarContratoRequest request,
                HttpContext http,
                IContratoServicoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                if (!request.Confirmo)
                    return Results.BadRequest(new { mensagem = "Você precisa confirmar que leu o contrato antes de assinar." });

                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                await service.AssinarAsync(id, userId.Value, ip);
                return Results.Ok(new { mensagem = "Assinatura registrada com sucesso." });
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // PDF — URL pré-assinada temporária (15 min), apenas para as partes
            // GET /api/contratos/{id}/pdf
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/contratos/{id:guid}/pdf", async (
                Guid id,
                HttpContext http,
                IContratoServicoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var url = await service.ObterUrlPdfAsync(id, userId.Value);
                return Results.Ok(new { url });
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // REGISTRAR ENTREGA — apenas prestador, contrato Ativo
            // POST /api/contratos/{id}/entrega
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/contratos/{id:guid}/entrega", async (
                Guid id,
                HttpContext http,
                IContratoServicoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.RegistrarEntregaAsync(id, userId.Value);
                return Results.Ok(new { mensagem = "Entrega registrada com sucesso." });
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // CANCELAR CONTRATO
            // DELETE /api/contratos/{id}
            // Situação 1 (Gerado/AguardandoAssinatura): gratuito, projeto reabre.
            // Situação 2 (Ativo, sem entrega): 5% taxa + 95% reembolso.
            // Situação 3 (Ativo, com entrega): bloqueado — deve abrir disputa.
            // ──────────────────────────────────────────────────────────────────────
            app.MapDelete("/api/contratos/{id:guid}", async (
                Guid id,
                string? motivo,
                HttpContext http,
                IContratoServicoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.CancelarAsync(id, userId.Value, motivo);
                return Results.Ok(new { mensagem = "Contrato cancelado." });
            }).RequireAuthorization();
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);

        private static string ExtrairTipoUsuario(HttpContext http)
        {
            return http.User.FindFirst(ClaimTypes.Role)?.Value
                ?? http.User.FindFirst("role")?.Value
                ?? string.Empty;
        }
    }
}
