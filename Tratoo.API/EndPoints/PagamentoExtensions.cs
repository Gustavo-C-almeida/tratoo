using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tratoo.API.EndPoints
{
    public static class PagamentoExtensions
    {
        public static void AddEndPointsPagamento(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // INICIAR PAGAMENTO
            // POST /api/pagamentos/iniciar
            // Contratante inicia o pagamento de um contrato ativo — retorna QR Code PIX
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/pagamentos/iniciar", async (
                IniciarPagamentoDto dto,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var resultado = await service.IniciarPagamentoAsync(dto.ContratoServicoId, userId.Value);
                return Results.Ok(resultado);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // OBTER QR CODE PIX
            // GET /api/pagamentos/{id}/pix
            // Retorna o QR Code PIX atualizado do pagamento
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/pagamentos/{id:guid}/pix", async (
                Guid id,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var resultado = await service.ObterPixAsync(id, userId.Value);
                return Results.Ok(resultado);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // DETALHE DO PAGAMENTO
            // GET /api/pagamentos/{id}
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/pagamentos/{id:guid}", async (
                Guid id,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var detalhe = await service.ObterDetalheAsync(id, userId.Value);
                return Results.Ok(detalhe);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // LIBERAR PAGAMENTO (aprovação da entrega pelo contratante)
            // POST /api/pagamentos/{id}/liberar
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/pagamentos/{id:guid}/liberar", async (
                Guid id,
                LiberarPagamentoDto dto,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var ip = http.Connection.RemoteIpAddress?.ToString();
                var userAgent = http.Request.Headers.UserAgent.ToString();

                var resultado = await service.LiberarPagamentoAsync(id, userId.Value, dto.ObservacaoContratante, ip, userAgent);
                return Results.Ok(resultado);
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // ABRIR DISPUTA
            // POST /api/pagamentos/{id}/disputar
            // Contratante ou prestador abre disputa sobre o escrow
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/pagamentos/{id:guid}/disputar", async (
                Guid id,
                AbrirDisputaDto dto,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var disputa = await service.AbrirDisputaAsync(id, userId.Value, dto);
                return Results.Ok(disputa);
            }).RequireAuthorization();

            // Resolução de disputa: ver área administrativa
            // POST /api/admin/disputas/{disputaId}/resolver (AdminDisputaExtensions, role Admin)

            // ──────────────────────────────────────────────────────────────────────
            // SOLICITAR ESTORNO
            // POST /api/pagamentos/{id}/estornar
            // Contratante solicita devolução (antes da liberação ao prestador)
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/pagamentos/{id:guid}/estornar", async (
                Guid id,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                await service.SolicitarEstornoAsync(id, userId.Value);
                return Results.Ok(new { mensagem = "Solicitação de estorno enviada ao gateway." });
            }).RequireAuthorization("Contratante");

            // ──────────────────────────────────────────────────────────────────────
            // SINCRONIZAR STATUS
            // GET /api/pagamentos/{id}/sincronizar
            // Consulta status atual no Asaas e atualiza o banco — alternativa ao webhook
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/api/pagamentos/{id:guid}/sincronizar", async (
                Guid id,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var resultado = await service.SincronizarStatusAsync(id, userId.Value);
                return Results.Ok(resultado);
            }).RequireAuthorization();

            // ──────────────────────────────────────────────────────────────────────
            // WEBHOOK ASAAS (público — para quando ngrok estiver configurado)
            // POST /api/webhooks/asaas
            // Recebe notificações em tempo real do Asaas sobre pagamentos e transferências
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/webhooks/asaas", async (
                HttpContext http,
                IPagamentoService service,
                IOptions<AsaasConfig> asaasOptions,
                ILogger<WebApplication> logger) =>
            {
                // ── Valida o token de autenticação do webhook ─────────────────
                // O Asaas envia o token configurado no painel via header
                // "asaas-access-token". Sem isso, qualquer um poderia forjar eventos
                // (ex: marcar um pagamento como recebido). Rejeita com 401 se não conferir.
                var tokenEsperado = asaasOptions.Value.WebhookToken;
                if (!string.IsNullOrWhiteSpace(tokenEsperado))
                {
                    var tokenRecebido = http.Request.Headers["asaas-access-token"].ToString();
                    var valido =
                        !string.IsNullOrEmpty(tokenRecebido) &&
                        CryptographicOperations.FixedTimeEquals(
                            Encoding.UTF8.GetBytes(tokenRecebido),
                            Encoding.UTF8.GetBytes(tokenEsperado));

                    if (!valido)
                    {
                        logger.LogWarning("Webhook Asaas rejeitado: token de autenticação ausente ou inválido.");
                        return Results.Unauthorized();
                    }
                }

                // ── Lê o payload ──────────────────────────────────────────────
                string payloadJson;
                try
                {
                    using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
                    payloadJson = await reader.ReadToEndAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao ler body do webhook Asaas.");
                    return Results.BadRequest(new { mensagem = "Payload inválido." });
                }

                if (string.IsNullOrWhiteSpace(payloadJson))
                    return Results.BadRequest(new { mensagem = "Payload vazio." });

                // ── Extrai tipo do evento ─────────────────────────────────────
                string? tipoEvento;
                try
                {
                    using var doc = JsonDocument.Parse(payloadJson);
                    tipoEvento = doc.RootElement.TryGetProperty("event", out var ev)
                        ? ev.GetString()
                        : null;
                }
                catch
                {
                    return Results.BadRequest(new { mensagem = "JSON inválido." });
                }

                if (string.IsNullOrWhiteSpace(tipoEvento))
                    return Results.BadRequest(new { mensagem = "Campo 'event' ausente no payload." });

                // ── Processa assincronamente — responde 200 imediatamente ─────
                // O Asaas espera resposta rápida. O processamento real ocorre aqui
                // mas erros internos NÃO devem retornar 5xx (forçaria reenvio desnecessário).
                try
                {
                    await service.ProcessarWebhookAsync(tipoEvento, payloadJson);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao processar webhook Asaas tipo {Evento}.", tipoEvento);
                    // Retorna 200 mesmo com erro para evitar loop de reenvio do Asaas.
                    // O erro está logado e no WebhookLog para análise posterior.
                }

                return Results.Ok(new { received = true, event_type = tipoEvento });
            });
            // webhook é público (sem RequireAuthorization) — segurança via token

            // ──────────────────────────────────────────────────────────────────────
            // REPROCESSAR TRANSFERÊNCIA FALHA (admin)
            // POST /api/admin/pagamentos/{id}/reprocessar-transferencia
            // Reverte FalhaTransferencia → Retido e inicia nova tentativa de liberação.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/admin/pagamentos/{id:guid}/reprocessar-transferencia", async (
                Guid id,
                HttpContext http,
                IPagamentoService service,
                ILogger<WebApplication> logger) =>
            {
                var adminId = ExtrairUserId(http);
                if (adminId == null) return Results.Unauthorized();

                logger.LogWarning(
                    "Admin {AdminId} solicitou reprocessamento da transferência do pagamento {PagamentoId}.",
                    adminId, id);

                var resultado = await service.ReprocessarTransferenciaAsync(id, adminId.Value);
                return Results.Ok(new
                {
                    mensagem    = "Reprocessamento iniciado. Aguardando confirmação via webhook TRANSFER_DONE.",
                    pagamentoId = resultado.Id,
                    statusAtual = resultado.Status.ToString()
                });
            }).RequireAuthorization("Admin");
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
