using Tratoo.API.Infrastructure;
using Tratoo.API.Requests;
using Tratoo.Domain.Exceptions;

namespace Tratoo.API.EndPoints
{
    /// <summary>
    /// Controle de sessão (<c>/api/auth</c>) e ações de segurança da conta (<c>/api/conta</c>).
    /// O login continua em <c>usuarios/login</c> e <c>usuarios/login/mfa</c>
    /// (<see cref="UserExtencions"/>), que agora criam a sessão.
    /// </summary>
    public static class AuthExtensions
    {
        public const string CodigoRefreshConcorrente = "REFRESH_CONCORRENTE";
        public const string CodigoSessaoAusente = "SESSAO_AUSENTE";

        public static void AddEndPointsAuth(this WebApplication app)
        {
            var auth = app.MapGroup("/api/auth");

            // ──────────────────────────────────────────
            // POST /api/auth/refresh — rotação do refresh token
            // Anônimo: o access token pode já ter expirado. Não conta como atividade.
            // ──────────────────────────────────────────
            auth.MapPost("/refresh", async (HttpContext http, SessaoService sessoes) =>
            {
                var resultado = await sessoes.RenovarAsync(http);
                switch (resultado.Status)
                {
                    case StatusRefresh.Renovado:
                        return Results.Ok(new { sessao = DescreverSessao(resultado.Sessao!, resultado.Politica!) });

                    case StatusRefresh.Concorrente:
                        return Results.Json(new
                        {
                            mensagem = "Outra aba renovou a sessão agora há pouco. Tente de novo.",
                            codigoErro = CodigoRefreshConcorrente
                        }, statusCode: StatusCodes.Status409Conflict);

                    case StatusRefresh.SemSessao:
                        return Results.Json(new
                        {
                            mensagem = "Nenhuma sessão ativa neste navegador.",
                            codigoErro = CodigoSessaoAusente
                        }, statusCode: StatusCodes.Status401Unauthorized);

                    default:
                        sessoes.LimparCookies(http);
                        return Results.Json(new
                        {
                            mensagem = "Sua sessão expirou. Entre novamente.",
                            codigoErro = SessaoAutenticacaoSetup.CodigoSessaoEncerrada
                        }, statusCode: StatusCodes.Status401Unauthorized);
                }
            })
            .AllowAnonymous()
            .NaoContaComoAtividade()
            .RequireRateLimiting(RateLimiterSetup.PoliticaRefresh);

            // ──────────────────────────────────────────
            // POST /api/auth/logout — encerra a sessão deste navegador
            // Anônimo pelo mesmo motivo do refresh: com o access expirado, o refresh
            // (cookie com Path=/api/auth) identifica a sessão.
            // ──────────────────────────────────────────
            auth.MapPost("/logout", Logout).AllowAnonymous().NaoContaComoAtividade();

            // ──────────────────────────────────────────
            // POST /api/auth/logout-todas — encerra todas as sessões do usuário
            // ──────────────────────────────────────────
            auth.MapPost("/logout-todas", async (HttpContext http, SessaoService sessoes) =>
            {
                var usuarioId = SessaoAtual.UsuarioIdDoToken(http.User);
                if (usuarioId is null) return Results.Unauthorized();

                var revogadas = await sessoes.RevogarTodasAsync(usuarioId.Value, SessaoService.MotivoLogoutTodas, ClientRequestInfo.ObterIp(http));
                sessoes.LimparCookies(http);
                return Results.Ok(new { mensagem = "Você saiu de todos os dispositivos.", revogadas });
            }).RequireAuthorization().RequireSessaoVerificada();

            // ──────────────────────────────────────────
            // GET /api/auth/sessoes — dispositivos conectados (marca a atual)
            // ──────────────────────────────────────────
            auth.MapGet("/sessoes", async (HttpContext http, SessaoService sessoes) =>
            {
                var usuarioId = SessaoAtual.UsuarioIdDoToken(http.User);
                if (usuarioId is null) return Results.Unauthorized();

                var atual = SessaoAtual.Sid(http);
                var lista = (await sessoes.ListarAsync(usuarioId.Value))
                    .OrderByDescending(s => s.Sid == atual)
                    .ThenByDescending(s => s.Sessao.UltimaAtividadeEm)
                    .Select(s => new
                    {
                        id = s.Sid,
                        atual = s.Sid == atual,
                        criadaEm = s.Sessao.CriadaEm,
                        ultimaAtividadeEm = s.Sessao.UltimaAtividadeEm,
                        expiraAbsolutaEm = s.Sessao.ExpiraEm,
                        manterConectado = s.Sessao.ManterConectado,
                        dispositivo = s.Sessao.UserAgent,
                        ip = s.Sessao.Ip
                    });

                return Results.Ok(lista);
            }).RequireAuthorization().RequireSessaoVerificada();

            // ──────────────────────────────────────────
            // DELETE /api/auth/sessoes/{id} — encerra UMA sessão (só do próprio usuário)
            // ──────────────────────────────────────────
            auth.MapDelete("/sessoes/{id}", async (string id, HttpContext http, SessaoService sessoes) =>
            {
                var usuarioId = SessaoAtual.UsuarioIdDoToken(http.User);
                if (usuarioId is null) return Results.Unauthorized();

                // Só sids do índice do próprio usuário: o id de outra pessoa dá 404, igual a
                // um id inexistente (não revela se existe).
                var doUsuario = await sessoes.ListarAsync(usuarioId.Value);
                if (!doUsuario.Any(s => s.Sid == id))
                    return Results.NotFound(new { mensagem = "Sessão não encontrada." });

                await sessoes.RevogarAsync(id, usuarioId.Value, SessaoService.MotivoEncerradaPeloUsuario, ClientRequestInfo.ObterIp(http));

                var eraAtual = id == SessaoAtual.Sid(http);
                if (eraAtual)
                    sessoes.LimparCookies(http);

                return Results.Ok(new { mensagem = "Sessão encerrada.", eraAtual });
            }).RequireAuthorization().RequireSessaoVerificada();

            // ──────────────────────────────────────────
            // POST /api/auth/atividade — interação real do usuário (204)
            // O registro em si é feito na validação do token (não passiva); esta rota só
            // existe para o frontend avisar "o usuário está aqui" sem outra chamada.
            // ──────────────────────────────────────────
            auth.MapPost("/atividade", () => Results.NoContent()).RequireAuthorization();

            // ──────────────────────────────────────────
            // POST /api/auth/reautenticar — senha (e código MFA, se ativo)
            // ──────────────────────────────────────────
            auth.MapPost("/reautenticar", async (ReautenticarRequest request, HttpContext http, SessaoService sessoes) =>
            {
                var usuarioId = SessaoAtual.UsuarioIdDoToken(http.User);
                var sid = SessaoAtual.Sid(http);
                if (usuarioId is null || sid is null) return Results.Unauthorized();

                var resultado = await sessoes.ReautenticarAsync(http, usuarioId.Value, sid, request.Senha, request.Codigo);
                if (resultado.RequerMFA)
                    return Results.Ok(new { requerMFA = true, mensagem = "Enviamos um código de verificação para o seu e-mail." });

                return Results.Ok(new { reautenticado = true });
            })
            .RequireAuthorization()
            .RequireSessaoVerificada()
            .RequireRateLimiting(RateLimiterSetup.PoliticaReautenticar);

            // ──────────────────────────────────────────
            // Conta: segurança (troca de senha, MFA)
            // ──────────────────────────────────────────
            var conta = app.MapGroup("/api/conta");

            conta.MapGet("/seguranca", async (HttpContext http, IUsuarioRepository usuarios) =>
            {
                var usuarioId = SessaoAtual.UsuarioIdDoToken(http.User);
                if (usuarioId is null) return Results.Unauthorized();

                var usuario = await usuarios.ObterPorIdAsync(usuarioId.Value);
                if (usuario is null) return Results.Unauthorized();

                return Results.Ok(new { mfaAtivo = usuario.MFA, mfaObrigatorio = usuario.IsAdmin });
            }).RequireAuthorization();

            // Troca voluntária: revoga as OUTRAS sessões e preserva a atual.
            conta.MapPost("/senha", async (AlterarSenhaRequest request, HttpContext http, IContaService contaService, SessaoService sessoes) =>
            {
                var usuarioId = SessaoAtual.UsuarioIdDoToken(http.User);
                if (usuarioId is null) return Results.Unauthorized();

                var ip = ClientRequestInfo.ObterIp(http);
                await contaService.AlterarSenhaAsync(usuarioId.Value, request.NovaSenha, request.ConfirmarNovaSenha, ip);
                var revogadas = await sessoes.RevogarTodasAsync(usuarioId.Value, SessaoService.MotivoSenhaAlterada, ip, preservarSid: SessaoAtual.Sid(http));

                return Results.Ok(new { mensagem = "Senha alterada. Os outros dispositivos foram desconectados.", revogadas });
            }).RequireAuthorization().ExigirReautenticacaoRecente();

            // Ativar/desativar MFA: revoga TODAS as sessões, inclusive esta.
            conta.MapPut("/mfa", async (AlterarMfaRequest request, HttpContext http, IContaService contaService, SessaoService sessoes) =>
            {
                var usuarioId = SessaoAtual.UsuarioIdDoToken(http.User);
                if (usuarioId is null) return Results.Unauthorized();

                var ip = ClientRequestInfo.ObterIp(http);
                await contaService.AlterarMfaAsync(usuarioId.Value, request.Ativo, ip);
                await sessoes.RevogarTodasAsync(usuarioId.Value, SessaoService.MotivoMfaAlterado, ip);
                sessoes.LimparCookies(http);

                return Results.Ok(new
                {
                    mensagem = request.Ativo ? "MFA ativado. Entre novamente." : "MFA desativado. Entre novamente.",
                    sessaoEncerrada = true
                });
            }).RequireAuthorization().ExigirReautenticacaoRecente();
        }

        /// <summary>Usado por /api/auth/logout e pelo antigo /usuarios/logout.</summary>
        public static async Task<IResult> Logout(HttpContext http, SessaoService sessoes, ILogger<SessaoService> logger)
        {
            try
            {
                await sessoes.EncerrarAtualAsync(http);
            }
            catch (ServicoIndisponivelException)
            {
                // Os cookies saem do navegador mesmo assim; a sessão no servidor expira
                // sozinha por inatividade. Logar e seguir: travar o logout seria pior.
                sessoes.LimparCookies(http);
                logger.LogWarning("Logout sem acesso ao armazenamento de sessões: cookies removidos, a sessão expira por inatividade.");
            }
            return Results.Ok(new { mensagem = "Logout realizado com sucesso" });
        }

        /// <summary>Datas que o frontend usa para o aviso "sua sessão expira em 2 min" (só UX; o servidor decide).</summary>
        public static object DescreverSessao(DadosSessao sessao, PoliticaSessao politica)
        {
            var (porInatividade, absoluta) = SessaoAtual.Expiracoes(sessao, politica);
            return new
            {
                expiraPorInatividadeEm = porInatividade,
                expiraAbsolutaEm = absoluta,
                janelaInatividadeSegundos = (long)politica.Inatividade.TotalSeconds,
                manterConectado = sessao.ManterConectado
            };
        }
    }
}
