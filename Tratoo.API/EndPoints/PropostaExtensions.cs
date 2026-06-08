using System.Security.Claims;
using Tratoo.Domain.Models;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class PropostaExtensions
    {
        public static void AddEndPointsProposta(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // RASCUNHO — prestador cria proposta como draft
            // POST /api/propostas — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/propostas", async (
                CriarRascunhoPropostaRequest request,
                HttpContext http,
                PropostaProjetoService service,
                IUsuarioRepository usuarioRepository,
                int projetoId) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                // Validar que o usuário não é um Contratante
                var usuario = await usuarioRepository.ObterPorIdAsync(userId.Value);
                if (usuario?.TipoUsuario == TipoUsuario.Contratante)
                    return Results.BadRequest(new { mensagem = "Contratantes não podem enviar propostas para projetos." });

                var dto = new CriarRascunhoPropostaDTO
                {
                    ProjetoId     = projetoId,
                    PrestadorId   = userId.Value,
                    ValidoAte     = request.ValidoAte,
                    Objetivo      = request.Objetivo,
                    Escopo        = request.Escopo,
                    Exclusoes     = request.Exclusoes,
                    RevisoesInclusas = request.RevisoesInclusas,
                    PrazoTotal    = request.PrazoTotal,
                    ValorTotal    = request.ValorTotal,
                    Entrada       = request.Entrada,
                    FormaPagamento = request.FormaPagamento,
                    Observacoes   = request.Observacoes,
                    MarcosJson    = request.MarcosJson
                };

                var proposta = await service.CriarRascunhoAsync(dto);
                return Results.Created($"/api/propostas/{proposta.Id}", proposta);
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // ENVIAR — draft → submitted
            // POST /api/propostas/{id}/enviar — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/propostas/{id:guid}/enviar", async (
                Guid id,
                HttpContext http,
                PropostaProjetoService service,
                IUsuarioRepository usuarioRepository) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                // Validar que o usuário não é um Contratante
                var usuario = await usuarioRepository.ObterPorIdAsync(userId.Value);
                if (usuario?.TipoUsuario == TipoUsuario.Contratante)
                    return Results.BadRequest(new { mensagem = "Contratantes não podem enviar propostas para projetos." });

                var proposta = await service.EnviarAsync(id, userId.Value);
                return Results.Ok(proposta);
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // ACEITAR PELA ÚLTIMA VERSÃO — prestador aceita contraproposta do contratante
            // no fluxo tradicional (quando última versão foi criada pelo contratante)
            // PUT /api/propostas/{id}/aceitar-negociacao — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapPut("/api/propostas/{id:guid}/aceitar-negociacao", async (
                Guid id,
                HttpContext http,
                PropostaProjetoService service,
                IUsuarioRepository usuarioRepository) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var usuario = await usuarioRepository.ObterPorIdAsync(userId.Value);
                if (usuario?.TipoUsuario == TipoUsuario.Contratante)
                    return Results.BadRequest(new { mensagem = "Apenas prestadores podem usar este endpoint." });

                var proposta = await service.AceitarUltimaVersaoAsync(id, userId.Value);
                return Results.Ok(proposta);
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // CONTRAPROPOSTA — cria nova versão (prestador ou contratante)
            // POST /api/propostas/{id}/contraproposta
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/propostas/{id:guid}/contraproposta", async (
                Guid id,
                ContrapropostaRequest request,
                HttpContext http,
                PropostaProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dto = new ContrapropostaDTO
                {
                    PropostaId       = id,
                    UsuarioId        = userId.Value,
                    Objetivo         = request.Objetivo,
                    Escopo           = request.Escopo,
                    Exclusoes        = request.Exclusoes,
                    RevisoesInclusas = request.RevisoesInclusas,
                    PrazoTotal       = request.PrazoTotal,
                    ValorTotal       = request.ValorTotal,
                    Entrada          = request.Entrada,
                    FormaPagamento   = request.FormaPagamento,
                    Observacoes      = request.Observacoes,
                    MarcosJson       = request.MarcosJson
                };

                var proposta = await service.ContrapropostaAsync(dto);
                return Results.Ok(proposta);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // ACEITAR — apenas o contratante dono do projeto
            // PUT /api/propostas/{id}/aceitar — somente Contratante
            // ──────────────────────────────────────────────────────────────────────
            app.MapPut("/api/propostas/{id:guid}/aceitar", async (
                Guid id,
                HttpContext http,
                PropostaProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var proposta = await service.AceitarAsync(id, userId.Value);
                return Results.Ok(proposta);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // RECUSAR — apenas o contratante
            // PUT /api/propostas/{id}/recusar — somente Contratante
            // ──────────────────────────────────────────────────────────────────────
            app.MapPut("/api/propostas/{id:guid}/recusar", async (
                Guid id,
                RecusarPropostaRequest request,
                HttpContext http,
                PropostaProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.RecusarAsync(id, userId.Value, request.Motivo);
                return Results.Ok(new { mensagem = "Proposta recusada." });
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // CANCELAR — prestador cancela própria proposta
            // DELETE /api/propostas/{id} — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapDelete("/api/propostas/{id:guid}", async (
                Guid id,
                HttpContext http,
                PropostaProjetoService service,
                string? motivo) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.CancelarAsync(id, userId.Value, motivo);
                return Results.Ok(new { mensagem = "Proposta cancelada." });
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // DETALHE — prestador ou contratante
            // GET /api/propostas/{id}
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/propostas/{id:guid}", async (
                Guid id,
                HttpContext http,
                PropostaProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var proposta = await service.ObterDetalheAsync(id, userId.Value);
                if (proposta == null) return Results.NotFound(new { mensagem = "Proposta não encontrada." });
                return Results.Ok(proposta);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // MINHAS PROPOSTAS — prestador logado
            // GET /api/me/propostas — somente Prestador
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/me/propostas", async (
                HttpContext http,
                PropostaProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var propostas = await service.ListarDoPrestadorAsync(userId.Value);
                return Results.Ok(propostas);
            }).RequireAuthorization("Prestador");
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
