using System.Security.Claims;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class ChatConviteExtensions
    {
        public static void AddEndPointsChatConvite(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // CRIAR PROPOSTA FORMAL (Contratante → Prestador, pós-convite aceito)
            // POST /api/convites/{conviteId}/proposta — somente Contratante
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/convites/{conviteId:guid}/proposta", async (
                Guid conviteId,
                CriarPropostaContratanteRequest request,
                HttpContext http,
                PropostaProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dto = new CriarRascunhoPropostaDTO
                {
                    ValidoAte = request.ValidoAte,
                    Objetivo = request.Objetivo,
                    Escopo = request.Escopo,
                    Exclusoes = request.Exclusoes,
                    RevisoesInclusas = request.RevisoesInclusas,
                    PrazoTotal = request.PrazoTotal,
                    ValorTotal = request.ValorTotal,
                    Entrada = request.Entrada,
                    FormaPagamento = request.FormaPagamento,
                    Observacoes = request.Observacoes,
                    MarcosJson = request.MarcosJson
                };

                var proposta = await service.CriarPropostaContratanteAsync(conviteId, userId.Value, dto);
                return Results.Created($"/api/propostas/{proposta.Id}", proposta);
            }).RequireAuthorization("Contratante");
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
