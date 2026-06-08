using Tratoo.Domain.Features.Perfis;

namespace Tratoo.API.EndPoints
{
    /// <summary>
    /// Endpoints de gerenciamento de dados bancários do prestador. Toda alteração exige
    /// revalidação de identidade por token enviado ao e-mail (uso único, 10 min) e está
    /// sob rate limiting. Apenas o próprio prestador acessa seus dados.
    /// </summary>
    public static class DadosBancariosExtensions
    {
        public static void AddEndPointsDadosBancarios(this WebApplication app)
        {
            // ──────────────────────────────────────────────────────────────────────
            // GET /prestadores/me/dados-bancarios — visão mascarada (nunca dados completos)
            // ──────────────────────────────────────────────────────────────────────
            app.MapGet("/prestadores/me/dados-bancarios", async (
                HttpContext http,
                IDadosBancariosService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var dados = await service.ObterAsync(userId.Value);
                return Results.Ok(dados);
            }).RequireAuthorization("Prestador");

            // ──────────────────────────────────────────────────────────────────────
            // POST /prestadores/me/dados-bancarios/solicitar-alteracao
            // Gera e envia o token de confirmação por e-mail.
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/prestadores/me/dados-bancarios/solicitar-alteracao", async (
                HttpContext http,
                IDadosBancariosService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                await service.SolicitarAlteracaoAsync(userId.Value, ip);

                return Results.Ok(new
                {
                    mensagem = "Enviamos um código de confirmação para o seu e-mail. Ele expira em 10 minutos."
                });
            }).RequireAuthorization("Prestador").RequireRateLimiting("dados-bancarios");

            // ──────────────────────────────────────────────────────────────────────
            // POST /prestadores/me/dados-bancarios/confirmar — valida token e libera edição
            // ──────────────────────────────────────────────────────────────────────
            app.MapPost("/prestadores/me/dados-bancarios/confirmar", async (
                ConfirmarTokenBancarioDTO dto,
                HttpContext http,
                IDadosBancariosService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                await service.ConfirmarTokenAsync(userId.Value, dto.Token, ip);

                return Results.Ok(new
                {
                    mensagem = "Código confirmado. Você pode salvar seus dados bancários nos próximos 10 minutos."
                });
            }).RequireAuthorization("Prestador").RequireRateLimiting("dados-bancarios");

            // ──────────────────────────────────────────────────────────────────────
            // PUT /prestadores/me/dados-bancarios — salva (exige token confirmado)
            // ──────────────────────────────────────────────────────────────────────
            app.MapPut("/prestadores/me/dados-bancarios", async (
                AtualizarDadosBancariosDTO dto,
                HttpContext http,
                IDadosBancariosService service) =>
            {
                var userId = ExtrairUserId(http);
                if (userId == null) return Results.Unauthorized();

                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                var dados = await service.AtualizarAsync(userId.Value, dto, ip);

                return Results.Ok(dados);
            }).RequireAuthorization("Prestador").RequireRateLimiting("dados-bancarios");
        }

        private static int? ExtrairUserId(HttpContext http) => ClaimsHelper.ExtrairUserId(http);
    }
}
