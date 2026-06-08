using System.Security.Claims;
using System.Text.Json;
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
            // SOLICITAR OTP PARA ASSINATURA
            // POST /api/contratos/{id}/assinar/solicitar-otp
            // Envia código de 6 dígitos ao e-mail do usuário. Válido 10 min, uso único.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/contratos/{id:guid}/assinar/solicitar-otp", async (
                Guid id,
                HttpContext http,
                IContratoServicoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var ip        = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                var userAgent = http.Request.Headers.UserAgent.ToString();

                await service.SolicitarOtpAssinaturaAsync(id, userId.Value, ip, userAgent);
                return Results.Ok(new { mensagem = "Código enviado ao seu e-mail. Válido por 10 minutos." });
            }).RequireAuthorization().RequireRateLimiting("otp-assinatura");

            // ──────────────────────────────────────────────────────────────────────
            // ASSINAR — assinatura digital do contrato (requer OTP)
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

                if (string.IsNullOrWhiteSpace(request.Otp))
                    return Results.BadRequest(new { mensagem = "Informe o código enviado ao seu e-mail." });

                var ip        = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                var userAgent = http.Request.Headers.UserAgent.ToString();

                await service.AssinarAsync(id, userId.Value, ip, userAgent, request.Otp);
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
            // REGISTRAR ENTREGA FORMAL — apenas prestador, contrato Ativo
            // POST /api/contratos/{id}/entrega  (multipart/form-data)
            // Campos: descricaoEntrega, observacoes?, dataEntrega, links? (JSON), files[]
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/contratos/{id:guid}/entrega", async (
                Guid id,
                HttpContext http,
                IEntregaService service,
                IR2PrivateStorageService storage) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                if (!http.Request.HasFormContentType)
                    return Results.BadRequest(new { mensagem = "Envie os dados como multipart/form-data." });

                var form = await http.Request.ReadFormAsync();

                var descricao = form["descricaoEntrega"].ToString();
                if (string.IsNullOrWhiteSpace(descricao))
                    return Results.BadRequest(new { mensagem = "A descrição da entrega é obrigatória." });

                var observacoes = form["observacoes"].ToString();
                DateTime dataEntrega = DateTime.UtcNow;
                if (DateTime.TryParse(form["dataEntrega"].ToString(), out var dt))
                    dataEntrega = dt.ToUniversalTime();

                // Links opcionais (JSON: [{ "url": "...", "descricao": "..." }])
                List<EntregaLinkDto>? links = null;
                var linksRaw = form["links"].ToString();
                if (!string.IsNullOrWhiteSpace(linksRaw))
                {
                    try
                    {
                        links = JsonSerializer.Deserialize<List<EntregaLinkDto>>(
                            linksRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch
                    {
                        return Results.BadRequest(new { mensagem = "Formato inválido dos links." });
                    }
                }

                // ── Validação de arquivos ─────────────────────────────────────
                var arquivos = form.Files;
                if (arquivos.Count > 10)
                    return Results.BadRequest(new { mensagem = "Máximo de 10 arquivos por entrega." });

                var extensoesPermitidas = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".zip" };
                const long tamanhoMax = 20L * 1024 * 1024;

                foreach (var arquivo in arquivos)
                {
                    var ext = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
                    if (!extensoesPermitidas.Contains(ext))
                        return Results.BadRequest(new { mensagem = $"Formato não permitido: {arquivo.FileName}. Use PDF, DOC, DOCX, XLS, XLSX, JPG, PNG ou ZIP." });
                    if (arquivo.Length > tamanhoMax)
                        return Results.BadRequest(new { mensagem = $"Arquivo muito grande: {arquivo.FileName}. Máximo 20 MB por arquivo." });
                }

                // ── Upload ao bucket privado ──────────────────────────────────
                var anexos = new List<EntregaAnexoUpload>();
                foreach (var arquivo in arquivos)
                {
                    var ext = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
                    var contentType = ContentTypePorExtensao(ext);
                    var chave = $"entregas/{id}/{Guid.NewGuid()}{ext}";

                    using var stream = arquivo.OpenReadStream();
                    await storage.UploadAsync(stream, chave, contentType);

                    anexos.Add(new EntregaAnexoUpload(
                        NomeArquivo: Path.GetFileName(arquivo.FileName),
                        ChaveR2: chave,
                        TipoArquivo: ext,
                        TamanhoArquivo: arquivo.Length));
                }

                var dto = new RegistrarEntregaDto(descricao, observacoes, dataEntrega, links);
                var resultado = await service.RegistrarEntregaAsync(id, userId.Value, dto, anexos);
                return Results.Ok(resultado);
            }).RequireAuthorization().DisableAntiforgery();

            // ──────────────────────────────────────────────────────────────────────
            // OBTER ENTREGA — qualquer parte do contrato (anexos com URL assinada)
            // GET /api/contratos/{id}/entrega
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/contratos/{id:guid}/entrega", async (
                Guid id,
                HttpContext http,
                IEntregaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var entrega = await service.ObterEntregaAsync(id, userId.Value);
                return entrega == null ? Results.NoContent() : Results.Ok(entrega);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // APROVAR ENTREGA — apenas contratante → libera pagamento
            // POST /api/contratos/{id}/entrega/aprovar
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/contratos/{id:guid}/entrega/aprovar", async (
                Guid id,
                HttpContext http,
                IEntregaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var ip = http.Connection.RemoteIpAddress?.ToString();
                var userAgent = http.Request.Headers.UserAgent.ToString();

                var resultado = await service.AprovarEntregaAsync(id, userId.Value, ip, userAgent);
                return Results.Ok(resultado);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // SOLICITAR AJUSTES (rejeitar) — apenas contratante
            // POST /api/contratos/{id}/entrega/rejeitar  body: { motivo }
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/contratos/{id:guid}/entrega/rejeitar", async (
                Guid id,
                RejeitarEntregaDto dto,
                HttpContext http,
                IEntregaService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var resultado = await service.RejeitarEntregaAsync(id, userId.Value, dto.Motivo);
                return Results.Ok(resultado);
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

        // ContentType derivado da extensão (nunca do cliente) para evitar type confusion.
        private static string ContentTypePorExtensao(string ext) => ext switch
        {
            ".pdf"  => "application/pdf",
            ".doc"  => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls"  => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"  => "image/png",
            ".zip"  => "application/zip",
            _       => "application/octet-stream"
        };
    }
}
