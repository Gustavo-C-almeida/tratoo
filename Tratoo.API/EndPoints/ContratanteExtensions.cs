using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class ContratanteExtensions
    {
        public static void AddEndPointsContratante(this WebApplication app)
        {
            // ── GET /contratantes/{id}/perfil — visualização pública (cache 2 min) ──
            app.MapGet("/contratantes/{id}/perfil", async (
                int id,
                PerfilContratanteService service,
                IMemoryCache cache) =>
            {
                var cacheKey = $"perfil_contratante_{id}";
                if (cache.TryGetValue(cacheKey, out PerfilPublicoContratanteDTO? cached))
                    return Results.Ok(cached);

                var dto = await service.PerfilPublicoAsync(id);
                cache.Set(cacheKey, dto, TimeSpan.FromMinutes(2));
                return Results.Ok(dto);
            });

            // ── GET /contratantes/me/perfil — próprio perfil (autenticado) ────
            app.MapGet("/contratantes/me/perfil", async (
                HttpContext http,
                PerfilContratanteService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dto = await service.MeuPerfilAsync(userId.Value);
                return Results.Ok(dto);
            }).RequireAuthorization("Contratante");

            // ── PUT /contratantes/me/perfil — atualizar perfil ────────────────
            app.MapPut("/contratantes/me/perfil", async (
                AtualizarPerfilContratanteRequest request,
                HttpContext http,
                PerfilContratanteService service,
                IMemoryCache cache) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.AtualizarPerfilAsync(new AtualizarPerfilContratanteDTO
                {
                    ContratanteId = userId.Value,
                    Descricao = request.Descricao,
                    SiteUrl = request.SiteUrl,
                    LinkedinUrl = request.LinkedinUrl,
                    Telefone = request.Telefone,
                    ExibirIdade = request.ExibirIdade
                });

                cache.Remove($"perfil_contratante_{userId.Value}");
                return Results.NoContent();
            }).RequireAuthorization("Contratante");

            // ── POST /contratantes/me/foto — upload foto de perfil ────────────
            app.MapPost("/contratantes/me/foto", async (
                HttpContext http,
                PerfilContratanteService service,
                IMemoryCache cache) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                if (!http.Request.HasFormContentType)
                    return Results.BadRequest(new { mensagem = "Envie a foto como multipart/form-data." });

                var form = await http.Request.ReadFormAsync();
                var arquivo = form.Files.GetFile("foto");

                if (arquivo == null)
                    return Results.BadRequest(new { mensagem = "Nenhum arquivo enviado. Use o campo 'foto'." });

                var extensoesPermitidas = new[] { ".jpg", ".jpeg", ".png" };
                var ext = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
                if (!extensoesPermitidas.Contains(ext))
                    return Results.BadRequest(new { mensagem = "Formato inválido. Use JPG ou PNG." });

                if (arquivo.Length > 2 * 1024 * 1024)
                    return Results.BadRequest(new { mensagem = "Arquivo muito grande. Máximo 2 MB." });

                // ContentType derivado da extensão (nunca do cliente) para evitar type confusion
                var contentTypeSeguro = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png"            => "image/png",
                    _                 => "application/octet-stream"
                };

                using var stream = arquivo.OpenReadStream();

                // Copia para MemoryStream para garantir tamanho conhecido (padrão R2StorageService)
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;

                var url = await service.AtualizarFotoAsync(userId.Value, ms, arquivo.FileName, contentTypeSeguro);

                cache.Remove($"perfil_contratante_{userId.Value}");
                return Results.Ok(new { url });
            }).RequireAuthorization("Contratante")
              .DisableAntiforgery();

            // ── DELETE /contratantes/me/foto — remover foto ───────────────────
            app.MapDelete("/contratantes/me/foto", async (
                HttpContext http,
                PerfilContratanteService service,
                IMemoryCache cache) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.RemoverFotoAsync(userId.Value);
                cache.Remove($"perfil_contratante_{userId.Value}");
                return Results.Ok(new { mensagem = "Foto removida com sucesso" });
            }).RequireAuthorization("Contratante");
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
