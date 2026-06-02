using System.Security.Claims;

namespace Tratoo.API.EndPoints
{
    public static class BuscaExtensions
    {
        public static void AddEndPointsBusca(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // GET /api/busca/prestadores
            // Busca semântica de prestadores com filtros e ranking composto.
            // Aberta ao público — sem autenticação.
            // Query: q, categoria, valorHoraMax, apenasVerificados, avaliacaoMin, page, pageSize
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/busca/prestadores", async (
                BuscaSemanticaService service,
                string? q,
                string? categoria,
                decimal? valorHoraMax,
                bool? apenasVerificados,
                double? avaliacaoMin,
                int? page,
                int? pageSize) =>
            {
                var filtros = new FiltrosBuscaPrestadoresDTO
                {
                    Q                 = q,
                    Categoria         = categoria,
                    ValorHoraMax      = valorHoraMax,
                    ApenasVerificados = apenasVerificados,
                    AvaliacaoMin      = avaliacaoMin,
                    Page              = page ?? 1,
                    PageSize          = pageSize ?? 20
                };

                var resultado = await service.BuscarPrestadoresAsync(filtros);
                return Results.Ok(resultado);
            });

            // ──────────────────────────────────────────────────────────────────────
            // GET /api/busca/prestadores/{id}/similares
            // Retorna prestadores com perfil semelhante ao de um prestador específico.
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/busca/prestadores/{id:int}/similares", async (
                int id,
                BuscaSemanticaService service,
                int? quantidade) =>
            {
                var resultado = await service.BuscarSimilaresAsync(id, quantidade ?? 5);
                return Results.Ok(resultado);
            });

            // ──────────────────────────────────────────────────────────────────────
            // GET /api/busca/projetos
            // Busca semântica de projetos abertos. Pública.
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/busca/projetos", async (
                BuscaSemanticaService service,
                string? q,
                int? page,
                int? pageSize) =>
            {
                var resultado = await service.BuscarProjetosAsync(q, page ?? 1, pageSize ?? 20);
                return Results.Ok(resultado);
            });

            // ──────────────────────────────────────────────────────────────────────
            // GET /api/busca/projetos/{id}/prestadores-recomendados
            // Prestadores mais adequados para um projeto específico do contratante,
            // rankeados por similaridade semântica + score composto.
            // Requer autenticação. O projeto deve pertencer ao contratante logado.
            // Query: quantidade (padrão 10, máx 20)
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/busca/projetos/{id:int}/prestadores-recomendados", async (
                int id,
                HttpContext http,
                BuscaSemanticaService service,
                int? quantidade) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var resultado = await service.RecomendarPrestadoresParaProjetoAsync(
                    id, userId.Value, quantidade ?? 10);

                if (resultado == null)
                    return Results.NotFound(new { mensagem = "Projeto não encontrado ou não pertence ao usuário." });

                return Results.Ok(resultado);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // GET /api/busca/projetos/recomendados
            // Projetos recomendados com base no perfil do prestador logado.
            // Requer autenticação.
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/busca/projetos/recomendados", async (
                HttpContext http,
                BuscaSemanticaService service,
                string? q) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var resultado = await service.RecomendarProjetosAsync(userId.Value, q);
                return Results.Ok(resultado);
            }).RequireAuthorization("Prestador");
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
