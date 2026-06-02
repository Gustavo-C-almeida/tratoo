using System.Security.Claims;
using System.Text;
using System.Text.Json;

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

                var resultado = await service.LiberarPagamentoAsync(id, userId.Value, dto.ObservacaoContratante);
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

            // ──────────────────────────────────────────────────────────────────────
            // RESOLVER DISPUTA (admin)
            // POST /api/pagamentos/{id}/disputas/{disputaId}/resolver
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/pagamentos/{id:guid}/disputas/{disputaId:guid}/resolver", async (
                Guid id,
                Guid disputaId,
                ResolverDisputaDto dto,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                // TODO: verificar se o usuário tem role de Admin
                await service.ResolverDisputaAsync(id, disputaId, userId.Value, dto);
                return Results.Ok(new { mensagem = "Disputa resolvida com sucesso." });
            }).RequireAuthorization();

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
            // SIMULAR PAGAMENTO PIX (sandbox/localhost apenas)
            // POST /api/pagamentos/{id}/simular
            // Confirma o pagamento diretamente via API Asaas Sandbox — substitui webhook
            // Use quando não há ngrok configurado para receber webhooks
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/api/pagamentos/{id:guid}/simular", async (
                Guid id,
                HttpContext http,
                IPagamentoService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var resultado = await service.SimularConfirmacaoAsync(id, userId.Value);
                return Results.Ok(resultado);
            }).RequireAuthorization();

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
                ILogger<WebApplication> logger) =>
            {
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
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
