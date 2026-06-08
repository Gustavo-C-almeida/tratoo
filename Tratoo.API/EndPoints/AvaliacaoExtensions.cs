using System.Security.Claims;

namespace Tratoo.API.EndPoints
{
    /// <summary>Payload para PUT /api/me/avaliacoes/privacidade (RN-AV-001).</summary>
    public record AlterarPrivacidadeAvaliacaoRequest(bool Privado);

    public static class AvaliacaoExtensions
    {
        public static void AddEndPointsAvaliacao(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // OBTER SLOT PENDENTE (para o avaliador logado)
            // GET /api/avaliacoes/{id}
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/avaliacoes/{id:guid}", async (
                Guid id,
                HttpContext http,
                IAvaliacaoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var pendentes = await service.ListarPendentesAsync(userId.Value);
                var av = pendentes.FirstOrDefault(p => p.Id == id);
                if (av == null)
                    return Results.NotFound(new { mensagem = "Avaliação não encontrada ou não pertence a você." });

                return Results.Ok(av);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // ENVIAR AVALIAÇÃO
            // POST /api/avaliacoes/{id}/enviar
            // Submete a nota e comentário do avaliador para o slot pendente.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/avaliacoes/{id:guid}/enviar", async (
                Guid id,
                EnviarAvaliacaoDto dto,
                HttpContext http,
                IAvaliacaoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.EnviarAvaliacaoAsync(id, userId.Value, dto);
                return Results.Ok(new { mensagem = "Avaliação enviada com sucesso." });
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // AVALIAÇÃO PENDENTE DO CONTRATO (para o usuário logado)
            // GET /api/contratos/{contratoId}/avaliacoes/pendente
            // Retorna o slot que o usuário logado deve preencher para o contrato.
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/contratos/{contratoId:guid}/avaliacoes/pendente", async (
                Guid contratoId,
                HttpContext http,
                IAvaliacaoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var pendente = await service.ObterPendenteDoContratoAsync(contratoId, userId.Value);
                if (pendente == null)
                    return Results.NotFound(new { mensagem = "Nenhuma avaliação pendente encontrada para este contrato." });

                return Results.Ok(pendente);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // ALTERAR PRIVACIDADE DAS AVALIAÇÕES (RN-AV-001/002/012)
            // PUT /api/me/avaliacoes/privacidade
            // Efeito imediato em perfil público, listagem e média (RN-AV-002).
            // ──────────────────────────────────────────────────────────────────────
            app.MapPut("/api/me/avaliacoes/privacidade", async (
                AlterarPrivacidadeAvaliacaoRequest request,
                HttpContext http,
                IAvaliacaoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.AlterarPrivacidadeAvaliacoesAsync(userId.Value, request.Privado);
                return Results.Ok(new { mensagem = "Preferência de privacidade atualizada com sucesso." });
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // REPUTAÇÃO PÚBLICA DE UM USUÁRIO
            // GET /api/usuarios/{id}/reputacao
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/usuarios/{id:int}/reputacao", async (
                int id,
                IAvaliacaoService service) =>
            {
                var reputacao = await service.ObterReputacaoAsync(id);
                return Results.Ok(reputacao);
            });

            // ──────────────────────────────────────────────────────────────────────
            // AVALIAÇÕES PÚBLICAS DE UM USUÁRIO (paginadas)
            // GET /api/usuarios/{id}/avaliacoes?pagina=1&tamanho=10
            // Retorna { avaliacoesVisiveis, avaliacoes } — quando privado, avaliacoesVisiveis=false
            // e avaliacoes fica vazio (RN-AV-005). Filtragem ocorre no backend (RN-AV-010/011).
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/usuarios/{id:int}/avaliacoes", async (
                int id,
                int pagina,
                int tamanho,
                IAvaliacaoService service) =>
            {
                pagina = Math.Max(1, pagina == 0 ? 1 : pagina);
                tamanho = Math.Clamp(tamanho == 0 ? 10 : tamanho, 1, 20);

                var resultado = await service.ListarPublicasAsync(id, pagina, tamanho);
                return Results.Ok(resultado);
            });
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
