using System.Security.Claims;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class ChatConviteExtensions
    {
        public static void AddEndPointsChatConvite(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // GET CHAT DO CONVITE — ambas as partes
            // GET /api/convites/{conviteId}/chat
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/convites/{conviteId:guid}/chat", async (
                Guid conviteId,
                HttpContext http,
                ChatConviteService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var chat = await service.ObterPorConviteAsync(conviteId, userId.Value);
                return Results.Ok(chat);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // GET MENSAGENS DO CHAT
            // GET /api/chats/{chatId}/mensagens?pagina=1
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/chats/{chatId:guid}/mensagens", async (
                Guid chatId,
                int pagina,
                HttpContext http,
                ChatConviteService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                if (pagina <= 0) pagina = 1;

                var (itens, total) = await service.ListarMensagensAsync(chatId, userId.Value, pagina);
                return Results.Ok(new { itens, total, pagina });
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // ENVIAR MENSAGEM NO CHAT
            // POST /api/chats/{chatId}/mensagens
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/chats/{chatId:guid}/mensagens", async (
                Guid chatId,
                EnviarChatMensagemRequest request,
                HttpContext http,
                ChatConviteService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dto = new EnviarChatMensagemDTO
                {
                    ChatId = chatId,
                    RemetenteId = userId.Value,
                    Texto = request.Texto
                };

                var mensagem = await service.EnviarMensagemAsync(dto);
                return Results.Created($"/api/chats/{chatId}/mensagens/{mensagem.Id}", mensagem);
            }).RequireAuthorization();

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

            // ──────────────────────────────────────────────────────────────────────
            // ACEITAR PROPOSTA DO CONTRATANTE (Prestador aceita → contrato gerado)
            // PUT /api/propostas/{id}/aceitar-prestador — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapPut("/api/propostas/{id:guid}/aceitar-prestador", async (
                Guid id,
                HttpContext http,
                PropostaProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var proposta = await service.AceitarPorPrestadorAsync(id, userId.Value);
                return Results.Ok(proposta);
            }).RequireAuthorization("Prestador");
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
