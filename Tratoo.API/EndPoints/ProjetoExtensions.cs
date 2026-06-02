using System.Security.Claims;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class ProjetoExtensions
    {
        public static void AddEndPointsProjeto(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // US-05: Busca pública de projetos com filtros e paginação
            // GET /api/projects
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/projects", async (
                ProjetoService service,
                string? q,
                CategoriaProjet? categoria,
                decimal? orcamentoMin,
                decimal? orcamentoMax,
                NivelFreelancerProjet? nivelFreelancer,
                DateTime? prazoAte,
                string? orderBy,
                int? page) =>
            {
                var filtros = new FiltrosProjetoDTO
                {
                    Q               = q,
                    Categoria       = categoria,
                    OrcamentoMin    = orcamentoMin,
                    OrcamentoMax    = orcamentoMax,
                    NivelFreelancer = nivelFreelancer,
                    PrazoAte        = prazoAte,
                    OrderBy         = orderBy ?? "recentes",
                    Page            = page ?? 1
                };

                var resultado = await service.BuscarAsync(filtros);
                return Results.Ok(resultado);
            });

            // GET /api/projects/{id}
            app.MapGet("/api/projects/{id:int}", async (int id, ProjetoService service) =>
            {
                var dto = await service.ObterDetalheAsync(id);
                if (dto == null) return Results.NotFound(new { mensagem = "Projeto não encontrado." });
                return Results.Ok(dto);
            });

            // ──────────────────────────────────────────────────────────────────────
            // US-04: Criar projeto (salva como RASCUNHO)
            // POST /api/projects — somente Contratante
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/projects", async (
                CriarProjetoRequest request,
                HttpContext http,
                ProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dto = new CriarProjetoDTO
                {
                    ContratanteId           = userId.Value,
                    Titulo                  = request.Titulo,
                    Descricao               = request.Descricao,
                    Categoria               = request.Categoria,
                    OrcamentoMin            = request.OrcamentoMin,
                    OrcamentoMax            = request.OrcamentoMax,
                    PrazoEntrega            = request.PrazoEntrega,
                    Habilidades             = request.Habilidades,
                    NivelFreelancer         = request.NivelFreelancer,
                    Visibilidade            = request.Visibilidade,
                    Idioma                  = request.Idioma,
                    NumFreelancersDesejados = request.NumFreelancersDesejados,
                    PublicarImediatamente   = request.PublicarImediatamente
                };

                var projeto = await service.CriarAsync(dto);
                return Results.Created($"/api/projects/{projeto.Id}", projeto);
            }).RequireAuthorization("Contratante");

            // POST /api/projects/{id}/publish — somente Contratante
            app.MapPost("/api/projects/{id:int}/publish", async (
                int id,
                HttpContext http,
                ProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.PublicarAsync(id, userId.Value);
                return Results.Ok(new { mensagem = "Projeto publicado com sucesso." });
            }).RequireAuthorization("Contratante");

            // DELETE /api/projects/{id} — somente Contratante
            app.MapDelete("/api/projects/{id:int}", async (
                int id,
                HttpContext http,
                ProjetoService service,
                string? motivo) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.CancelarAsync(id, userId.Value, motivo);
                return Results.Ok(new { mensagem = "Projeto cancelado com sucesso." });
            }).RequireAuthorization("Contratante");

            // GET /api/me/projects — somente Contratante
            app.MapGet("/api/me/projects", async (
                HttpContext http,
                ProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var projetos = await service.GetDoContratanteAsync(userId.Value);
                return Results.Ok(projetos);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // Propostas do projeto — leitura para o contratante dono do projeto
            // GET /api/projects/{id}/proposals — somente Contratante
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/projects/{id:int}/proposals", async (
                int id,
                HttpContext http,
                PropostaProjetoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var propostas = await service.ListarDoProjetoAsync(id, userId.Value);
                return Results.Ok(propostas);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // Chat do projeto — isolado por par (contratante + prestador)
            // GET  /api/projects/{id}/mensagens?prestadorId=X&page=1
            // POST /api/projects/{id}/mensagens  (body: { texto, prestadorId })
            // GET  /api/me/chats                 (lista de chats do usuário logado)
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/projects/{id:int}/mensagens", async (
                int id,
                HttpContext http,
                MensagemProjetoService service,
                ProjetoService projetoService,
                PropostaProjetoService propostaService,
                int? prestadorId,
                int? page) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var projeto = await projetoService.ObterDetalheAsync(id);
                if (projeto == null) return Results.NotFound(new { mensagem = "Projeto não encontrado." });

                int parceiroPrestadorId;

                if (projeto.ContratanteId == userId.Value)
                {
                    // Contratante deve informar qual prestador quer consultar
                    if (prestadorId == null)
                        return Results.BadRequest(new { mensagem = "Informe o prestadorId para acessar o chat." });
                    // Valida que esse prestador tem proposta no projeto
                    var propostas = await propostaService.ListarDoProjetoAsync(id, userId.Value);
                    if (!propostas.Any(p => p.PrestadorId == prestadorId.Value))
                        return Results.Json(new { mensagem = "Este prestador não tem proposta neste projeto." }, statusCode: 403);
                    parceiroPrestadorId = prestadorId.Value;
                }
                else
                {
                    // Prestador acessa o próprio chat
                    var minhasPropostas = await propostaService.ListarDoPrestadorAsync(userId.Value);
                    if (!minhasPropostas.Any(p => p.ProjetoId == id))
                        return Results.Json(new { mensagem = "Acesso negado a este chat." }, statusCode: 403);
                    parceiroPrestadorId = userId.Value;
                }

                var (itens, total) = await service.ListarAsync(id, parceiroPrestadorId, page ?? 1);
                return Results.Ok(new { total, pagina = page ?? 1, itens });
            }).RequireAuthorization();

            app.MapPost("/api/projects/{id:int}/mensagens", async (
                int id,
                MensagemProjetoRequest request,
                HttpContext http,
                MensagemProjetoService service,
                ProjetoService projetoService,
                PropostaProjetoService propostaService) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var projeto = await projetoService.ObterDetalheAsync(id);
                if (projeto == null) return Results.NotFound(new { mensagem = "Projeto não encontrado." });

                int parceiroPrestadorId;

                if (projeto.ContratanteId == userId.Value)
                {
                    // Contratante envia mensagem para um prestador específico
                    if (request.PrestadorId == null)
                        return Results.BadRequest(new { mensagem = "Informe o prestadorId no corpo da mensagem." });
                    var propostas = await propostaService.ListarDoProjetoAsync(id, userId.Value);
                    if (!propostas.Any(p => p.PrestadorId == request.PrestadorId.Value))
                        return Results.Json(new { mensagem = "Este prestador não tem proposta neste projeto." }, statusCode: 403);
                    parceiroPrestadorId = request.PrestadorId.Value;
                }
                else
                {
                    var minhasPropostas = await propostaService.ListarDoPrestadorAsync(userId.Value);
                    if (!minhasPropostas.Any(p => p.ProjetoId == id))
                        return Results.Json(new { mensagem = "Acesso negado a este chat." }, statusCode: 403);
                    parceiroPrestadorId = userId.Value;
                }

                var dto = new EnviarMensagemProjetoDTO
                {
                    ProjetoId        = id,
                    PrestadorId      = parceiroPrestadorId,
                    RemetenteId      = userId.Value,
                    Texto            = request.Texto,
                    Tipo             = request.Tipo,
                    PropostaVersaoId = request.PropostaVersaoId
                };

                var mensagem = await service.EnviarAsync(dto);
                return Results.Created($"/api/projects/{id}/mensagens", mensagem);
            }).RequireAuthorization();

            // Lista centralizada de chats do usuário logado
            app.MapGet("/api/me/chats", async (
                HttpContext http,
                MensagemProjetoService service,
                IUsuarioRepository usuarioRepo) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var usuario = await usuarioRepo.ObterPorIdAsync(userId.Value);
                if (usuario == null) return Results.Unauthorized();

                if (usuario.TipoUsuario == TipoUsuario.Contratante)
                {
                    var chats = await service.ListarChatsContratanteAsync(userId.Value);
                    return Results.Ok(chats);
                }
                else
                {
                    var chats = await service.ListarChatsPrestadorAsync(userId.Value);
                    return Results.Ok(chats);
                }
            }).RequireAuthorization();
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
