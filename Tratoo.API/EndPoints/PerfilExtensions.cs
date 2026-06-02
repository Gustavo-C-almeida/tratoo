using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Tratoo.API.Requests;

namespace Tratoo.API.EndPoints
{
    public static class PerfilExtensions
    {
        public static void AddEndPointsPerfil(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // PERFIL
            // ──────────────────────────────────────────────────────────────────────

            // GET /prestadores/{id}/perfil  — visualização pública (cache de 2 min)

            app.MapGet("/prestadores/{id}/perfil", async (
                int id,
                IPrestadorRepository repo,
                IIdentidadeRepository identidadeRepo,
                IMemoryCache cache) =>
            {
                var cacheKey = $"perfil_publico_{id}";
                if (cache.TryGetValue(cacheKey, out PerfilPublicoDTO? cached))
                    return Results.Ok(cached);

                var p = await repo.GetCompletoAsync(id);
                if (p == null) return Results.NotFound(new { mensagem = "Prestador não encontrado" });

                var identity = await identidadeRepo.ObterPorUserIdAsync(id);

                var dto = new PerfilPublicoDTO
                {
                    Id                  = p.Id,
                    Nome                = p.Nome,
                    TituloProfissional  = p.TituloProfissional,
                    Bio                 = p.Descricao,
                    FotoUrl             = p.FotoUrl,
                    AreaEspecializacao  = p.AreaEspecializacao,
                    ValorHora           = p.ValorHora,
                    Disponivel          = p.Disponivel,
                    LinkedinUrl         = p.LinkedinUrl,
                    GitHubUrl           = p.GitHubUrl,
                    OutrosLinks         = p.OutrosLinks,
                    LocalizacaoCidade   = p.Endereco?.Cidade,
                    LocalizacaoEstado   = p.Endereco?.Estado,
                    PorcentagemCompleto = p.PorcentagemCompleto,
                    NivelVerificacao    = identity?.NivelVerificacao,

                    Competencias = p.Competencias.Select(c => new CompetenciaDTO
                    {
                        Id    = c.Id,
                        Nome  = c.Nome,
                        Nivel = c.Nivel
                    }).ToList(),

                    Certificacoes = p.Certificacoes.Select(c => new CertificacaoDTO
                    {
                        Id              = c.Id,
                        Nome            = c.Nome,
                        Instituicao     = c.InstituicaoEmissora,
                        DataEmissao     = c.DataEmissao,
                        DataValidade    = c.DataValidade,
                        LinkVerificacao = c.LinkVerificacao,
                        Competencias    = c.CompetenciaCertificacoes.Select(cc => new CompetenciaDTO
                        {
                            Id    = cc.Competencia.Id,
                            Nome  = cc.Competencia.Nome,
                            Nivel = cc.Competencia.Nivel
                        }).ToList()
                    }).ToList(),

                    Experiencias = p.Experiencias.Select(e => new ExperienciaDTO
                    {
                        Id           = e.Id,
                        Cargo        = e.Cargo,
                        Empresa      = e.Empresa,
                        Atividades   = e.Atividades,
                        DataInicio   = e.DataInicio,
                        DataFim      = e.DataFim,
                        EmpregoAtual = e.EmpregoAtual,
                        Local        = e.Local,
                        TipoContrato = e.TipoContrato,
                        Competencias = e.CompetenciaExperiencias.Select(ce => new CompetenciaDTO
                        {
                            Id    = ce.Competencia.Id,
                            Nome  = ce.Competencia.Nome,
                            Nivel = ce.Competencia.Nivel
                        }).ToList()
                    }).ToList(),

                    Portfolio = p.Portfolio.Select(pt => new PortfolioDTO
                    {
                        Id          = pt.Id,
                        PrestadorId = pt.PrestadorId,
                        Titulo      = pt.Titulo,
                        Descricao   = pt.Descricao,
                        LinkExterno = pt.LinkExterno,
                        ArquivoUrl  = pt.ArquivoUrl,
                        CriadoEm   = pt.CriadoEm,
                        Competencias = pt.CompetenciaPortfolios.Select(cp => new CompetenciaDTO
                        {
                            Id    = cp.Competencia.Id,
                            Nome  = cp.Competencia.Nome,
                            Nivel = cp.Competencia.Nivel
                        }).ToList()
                    }).ToList()
                };

                cache.Set(cacheKey, dto, TimeSpan.FromMinutes(2));
                return Results.Ok(dto);
            });

            // GET /prestadores/me/perfil  — próprio perfil (somente Prestador)
            app.MapGet("/prestadores/me/perfil", async (
                HttpContext http,
                MyOwnProfilePrestadorService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dto = await service.VisualizarAsync(userId.Value);
                return Results.Ok(dto);
            }).RequireAuthorization("Prestador");

            // PUT /prestadores/me/perfil  — atualizar perfil (somente Prestador)
            app.MapPut("/prestadores/me/perfil", async (
                AtualizarPerfilRequest request,
                HttpContext http,
                PerfilProfissaoPrestadorService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.AtualizarPerfilAsync(new UpdateProfissaoPrestadorDTO
                {
                    PrestadorId       = userId.Value,
                    TituloProfissional = request.TituloProfissional,
                    AreaEspecializacao = request.AreaEspecializacao,
                    FuncaoExecutada    = request.FuncaoExecutada,
                    Descricao          = request.Descricao,
                    LinkedinUrl        = request.LinkedinUrl,
                    PortfolioUrl       = request.PortfolioUrl,
                    GitHubUrl          = request.GitHubUrl,
                    EmailContato       = request.EmailContato,
                    OutrosLinks        = request.OutrosLinks,
                    ValorHora          = request.ValorHora,
                    Telefone           = request.Telefone
                });

                return Results.NoContent();
            }).RequireAuthorization("Prestador");

            // POST /prestadores/me/foto  — upload de foto de perfil
            app.MapPost("/prestadores/me/foto", async (
                HttpContext http,
                IArquivoStorageService storage,
                PerfilProfissaoPrestadorService perfilService) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                if (!http.Request.HasFormContentType)
                    return Results.BadRequest(new { mensagem = "Envie a foto como multipart/form-data." });

                var form    = await http.Request.ReadFormAsync();
                var arquivo = form.Files.GetFile("foto");

                if (arquivo == null)
                    return Results.BadRequest(new { mensagem = "Nenhum arquivo enviado. Use o campo 'foto'." });

                var extensoesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var ext = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
                if (!extensoesPermitidas.Contains(ext))
                    return Results.BadRequest(new { mensagem = "Formato inválido. Use JPG, PNG ou WebP." });

                if (arquivo.Length > 5 * 1024 * 1024)
                    return Results.BadRequest(new { mensagem = "Arquivo muito grande. Máximo 5 MB." });

                // ContentType derivado da extensão (nunca do cliente) para evitar type confusion
                var contentTypeSeguro = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png"            => "image/png",
                    ".webp"           => "image/webp",
                    _                 => "application/octet-stream"
                };

                var chave = $"fotos/{userId.Value}_{Guid.NewGuid()}{ext}";
                using var stream = arquivo.OpenReadStream();
                var url = await storage.UploadAsync(stream, chave, contentTypeSeguro);

                await perfilService.AtualizarFotoAsync(userId.Value, url);

                return Results.Ok(new { url });
            }).RequireAuthorization("Prestador")
              .DisableAntiforgery();

            // ──────────────────────────────────────────────────────────────────────
            // PORTFÓLIO
            // ──────────────────────────────────────────────────────────────────────

            app.MapGet("/prestadores/{id}/portfolio", async (
                int id,
                PortfolioService service) =>
            {
                var lista = await service.VisualizarAsync(id);
                return Results.Ok(lista);
            });

            app.MapPost("/prestadores/me/portfolio", async (
                HttpContext http,
                IArquivoStorageService storage,
                PortfolioService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                string? titulo, descricao, linkExterno, arquivoUrl = null;

                if (http.Request.HasFormContentType)
                {
                    var form = await http.Request.ReadFormAsync();
                    titulo      = NullIfEmpty(form["titulo"].ToString());
                    descricao   = NullIfEmpty(form["descricao"].ToString());
                    linkExterno = NullIfEmpty(form["linkExterno"].ToString());

                    var arquivo = form.Files.GetFile("arquivo");
                    if (arquivo != null)
                    {
                        var ext = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
                        if (ext != ".pdf")
                            return Results.BadRequest(new { mensagem = "Somente arquivos PDF são aceitos." });

                        if (arquivo.Length > 10 * 1024 * 1024)
                            return Results.BadRequest(new { mensagem = "Arquivo muito grande. Máximo 10 MB." });

                        var chave = $"portfolio/{userId.Value}_{Guid.NewGuid()}.pdf";
                        using var stream = arquivo.OpenReadStream();
                        arquivoUrl = await storage.UploadAsync(stream, chave, "application/pdf");
                    }
                }
                else
                {
                    var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body);
                    titulo      = body.RootElement.GetProperty("titulo").GetString() ?? string.Empty;
                    descricao   = body.RootElement.TryGetProperty("descricao",   out var d) ? d.GetString() : null;
                    linkExterno = body.RootElement.TryGetProperty("linkExterno", out var l) ? l.GetString() : null;
                }

                if (string.IsNullOrWhiteSpace(titulo))
                    return Results.BadRequest(new { mensagem = "O título do portfólio é obrigatório." });

                if (titulo!.Length > 120)
                    return Results.BadRequest(new { mensagem = "Título deve ter no máximo 120 caracteres." });

                if (descricao?.Length > 500)
                    return Results.BadRequest(new { mensagem = "Descrição deve ter no máximo 500 caracteres." });

                var dto = new PortfolioDTO
                {
                    PrestadorId = userId.Value,
                    Titulo      = titulo!,
                    Descricao   = descricao,
                    LinkExterno = linkExterno,
                    ArquivoUrl  = arquivoUrl
                };

                var criado = await service.AdicionarAsync(dto);
                return Results.Created($"/prestadores/{userId.Value}/portfolio/{criado.Id}", criado);
            }).RequireAuthorization("Prestador")
              .DisableAntiforgery();

            app.MapPut("/prestadores/me/portfolio/{id}", async (
                int id,
                HttpContext http,
                IArquivoStorageService storage,
                PortfolioService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                string? titulo, descricao, linkExterno, arquivoUrl = null;

                if (http.Request.HasFormContentType)
                {
                    var form = await http.Request.ReadFormAsync();
                    titulo      = NullIfEmpty(form["titulo"].ToString());
                    descricao   = NullIfEmpty(form["descricao"].ToString());
                    linkExterno = NullIfEmpty(form["linkExterno"].ToString());
                    arquivoUrl  = NullIfEmpty(form["arquivoUrlExistente"].ToString()); // manter existente se não enviar novo

                    var arquivo = form.Files.GetFile("arquivo");
                    if (arquivo != null)
                    {
                        var ext = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
                        if (ext != ".pdf")
                            return Results.BadRequest(new { mensagem = "Somente arquivos PDF são aceitos." });

                        var chave = $"portfolio/{userId.Value}_{Guid.NewGuid()}.pdf";
                        using var stream = arquivo.OpenReadStream();
                        arquivoUrl = await storage.UploadAsync(stream, chave, "application/pdf");
                    }
                }
                else
                {
                    var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body);
                    titulo      = body.RootElement.GetProperty("titulo").GetString() ?? string.Empty;
                    descricao   = body.RootElement.TryGetProperty("descricao",   out var d) ? d.GetString() : null;
                    linkExterno = body.RootElement.TryGetProperty("linkExterno", out var l) ? l.GetString() : null;
                    arquivoUrl  = body.RootElement.TryGetProperty("arquivoUrl",  out var a) ? a.GetString() : null;
                }

                if (string.IsNullOrWhiteSpace(titulo))
                    return Results.BadRequest(new { mensagem = "O título do portfólio é obrigatório." });

                if (titulo!.Length > 120)
                    return Results.BadRequest(new { mensagem = "Título deve ter no máximo 120 caracteres." });

                if (descricao?.Length > 500)
                    return Results.BadRequest(new { mensagem = "Descrição deve ter no máximo 500 caracteres." });

                await service.EditarAsync(new PortfolioDTO
                {
                    Id          = id,
                    PrestadorId = userId.Value,
                    Titulo      = titulo!,
                    Descricao   = descricao,
                    LinkExterno = linkExterno,
                    ArquivoUrl  = arquivoUrl
                });

                return Results.NoContent();
            }).RequireAuthorization("Prestador")
              .DisableAntiforgery();

            app.MapDelete("/prestadores/me/portfolio/{id}", async (
                int id,
                HttpContext http,
                PortfolioService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.RemoverAsync(id, userId.Value);
                return Results.Ok(new { mensagem = "Item de portfólio removido com sucesso" });
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // EXPERIÊNCIAS
            // ──────────────────────────────────────────────────────────────────────

            app.MapGet("/prestadores/{id}/experiencias", async (
                int id,
                ExperienciaService service) =>
            {
                var lista = await service.VisualizarAsync(id);
                return Results.Ok(lista);
            });

            app.MapPost("/prestadores/me/experiencias", async (
                AdicionarExperienciaRequest request,
                HttpContext http,
                ExperienciaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.AdicionarAsync(new ExperienciaDTO
                {
                    PrestadorId  = userId.Value,
                    Empresa      = request.Empresa,
                    Cargo        = request.Cargo,
                    Atividades   = request.Atividades,
                    DataInicio   = request.DataInicio,
                    DataFim      = request.DataFim,
                    EmpregoAtual = request.EmpregoAtual,
                    Local        = request.Local,
                    TipoContrato = request.TipoContrato
                });

                return Results.Created($"/prestadores/{userId.Value}/experiencias", null);
            }).RequireAuthorization("Prestador");

            app.MapPut("/prestadores/me/experiencias/{id}", async (
                int id,
                AtualizarExperienciaRequest request,
                HttpContext http,
                ExperienciaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.EditarAsync(new ExperienciaDTO
                {
                    Id           = id,
                    PrestadorId  = userId.Value,
                    Empresa      = request.Empresa,
                    Cargo        = request.Cargo,
                    Atividades   = request.Atividades,
                    DataInicio   = request.DataInicio,
                    DataFim      = request.DataFim,
                    EmpregoAtual = request.EmpregoAtual,
                    Local        = request.Local,
                    TipoContrato = request.TipoContrato
                });

                return Results.NoContent();
            }).RequireAuthorization("Prestador");

            app.MapDelete("/prestadores/me/experiencias/{id}", async (
                int id,
                HttpContext http,
                ExperienciaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.RemoverAsync(id, userId.Value);
                return Results.Ok(new { mensagem = "Experiência removida com sucesso" });
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // CERTIFICAÇÕES
            // ──────────────────────────────────────────────────────────────────────

            app.MapGet("/prestadores/{id}/certificacoes", async (
                int id,
                CertificacaoService service) =>
            {
                var lista = await service.VisualizarAsync(id);
                return Results.Ok(lista);
            });

            app.MapPost("/prestadores/me/certificacoes", async (
                AdicionarCertificacaoRequest request,
                HttpContext http,
                CertificacaoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.AdicionarAsync(new CertificacaoDTO
                {
                    PrestadorId     = userId.Value,
                    Nome            = request.Nome,
                    Instituicao     = request.Instituicao,
                    DataEmissao     = request.DataEmissao,
                    DataValidade    = request.DataValidade,
                    LinkVerificacao = request.LinkVerificacao
                });

                return Results.Created($"/prestadores/{userId.Value}/certificacoes", null);
            }).RequireAuthorization("Prestador");

            app.MapPut("/prestadores/me/certificacoes/{id}", async (
                int id,
                AtualizarCertificacaoRequest request,
                HttpContext http,
                CertificacaoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.EditarAsync(new CertificacaoDTO
                {
                    Id              = id,
                    PrestadorId     = userId.Value,
                    Nome            = request.Nome,
                    Instituicao     = request.Instituicao,
                    DataEmissao     = request.DataEmissao,
                    DataValidade    = request.DataValidade,
                    LinkVerificacao = request.LinkVerificacao
                });

                return Results.NoContent();
            }).RequireAuthorization("Prestador");

            app.MapDelete("/prestadores/me/certificacoes/{id}", async (
                int id,
                HttpContext http,
                CertificacaoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.RemoverAsync(id, userId.Value);
                return Results.Ok(new { mensagem = "Certificação removida com sucesso" });
            }).RequireAuthorization("Prestador");
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);

        private static string? NullIfEmpty(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
