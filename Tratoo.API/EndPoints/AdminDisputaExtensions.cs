using Tratoo.Domain.Features.Pagamentos;
using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.API.EndPoints
{
    /// <summary>
    /// Área administrativa de disputas. Todos os endpoints exigem a role "Admin"
    /// (concedida apenas via seed/banco — nunca por fluxo da aplicação).
    /// </summary>
    public static class AdminDisputaExtensions
    {
        public static void AddEndPointsAdminDisputa(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // LISTAR DISPUTAS (com filtros) — GET /api/admin/disputas
            // Filtros: status, de, ate (datas), contratanteId, prestadorId, projetoId
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/admin/disputas", async (
                HttpContext http,
                AdminDisputaService service,
                string? status,
                DateTime? de,
                DateTime? ate,
                int? contratanteId,
                int? prestadorId,
                int? projetoId) =>
            {
                StatusDisputa? statusEnum = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (!Enum.TryParse<StatusDisputa>(status, true, out var parsed))
                        return Results.BadRequest(new { mensagem = "Status de disputa inválido." });
                    statusEnum = parsed;
                }

                var disputas = await service.ListarAsync(statusEnum, de, ate, contratanteId, prestadorId, projetoId);
                return Results.Ok(disputas);
            }).RequireAuthorization("Admin");

            // ──────────────────────────────────────────────────────────────────────
            // DETALHE DA DISPUTA — GET /api/admin/disputas/{disputaId}
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/admin/disputas/{disputaId:guid}", async (
                Guid disputaId,
                AdminDisputaService service) =>
            {
                var detalhe = await service.ObterDetalheAsync(disputaId);
                return Results.Ok(detalhe);
            }).RequireAuthorization("Admin");

            // ──────────────────────────────────────────────────────────────────────
            // RESOLVER DISPUTA — POST /api/admin/disputas/{disputaId}/resolver
            // FavorContratante=true → estorno + contrato cancelado
            // FavorContratante=false → liberação + contrato encerrado (concluído)
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/admin/disputas/{disputaId:guid}/resolver", async (
                Guid disputaId,
                ResolverDisputaAdminRequest request,
                HttpContext http,
                AdminDisputaService service) =>
            {
                var userIdStr = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                             ?? http.User.FindFirst("sub")?.Value;
                if (!int.TryParse(userIdStr, out var adminId))
                    return Results.Unauthorized();

                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "admin";

                await service.ResolverAsync(disputaId, adminId, request, ip);
                return Results.Ok(new { mensagem = "Disputa resolvida com sucesso." });
            }).RequireAuthorization("Admin");
        }
    }
}
