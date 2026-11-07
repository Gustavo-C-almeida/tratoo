using System.Security.Cryptography;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Models;

namespace Tratoo.API.Infrastructure
{
    /// <summary>Quem está entrando (vem do login, do MFA ou do banco no refresh).</summary>
    public sealed record UsuarioSessao(int Id, string Email, string Nome, string Tipo, bool PerfilCompleto, bool IsAdmin)
    {
        public string Papel => IsAdmin ? "Admin" : Tipo;

        public static UsuarioSessao De(LoginResponseDTO r) =>
            new(r.UsuarioId, r.Email, r.Nome, r.Tipo, r.PerfilMinimoCompleto, r.IsAdmin);

        public static UsuarioSessao De(Usuario u) =>
            new(u.Id, u.Email, u.Nome, u.TipoUsuario.ToString(), u.PerfilMinimoCompleto, u.IsAdmin);
    }

    public enum StatusRefresh
    {
        /// <summary>O navegador nem mandou cookie de refresh (visitante, ou cookie já apagado). 401.</summary>
        SemSessao,
        /// <summary>Token rotacionado; cookies novos emitidos.</summary>
        Renovado,
        /// <summary>Outra requisição com o mesmo token venceu há pouco (dentro da tolerância). 409, sem revogar.</summary>
        Concorrente,
        /// <summary>Token desconhecido ou malformado. 401, sem revogar nada.</summary>
        Desconhecido,
        /// <summary>Token já rotacionado reapresentado fora da tolerância: sessão revogada. 401.</summary>
        ReusoDetectado,
        /// <summary>A sessão não existe mais, passou da expiração absoluta ou a conta não pode entrar. 401.</summary>
        SessaoEncerrada
    }

    public sealed record ResultadoRefresh(StatusRefresh Status, DadosSessao? Sessao = null, PoliticaSessao? Politica = null);

    public sealed record ResultadoReautenticacao(bool RequerMFA);

    /// <summary>
    /// Ciclo de vida da sessão: criação (login/MFA), rotação do refresh token, revogação,
    /// listagem e reautenticação. A sessão vive no <see cref="ISessaoStore"/>; os eventos vão
    /// para o <c>AuditLog</c> no Postgres (motivo dentro da ação, ex.:
    /// <c>sessao_revogada:reuso_detectado</c> — sem migration).
    /// </summary>
    public sealed class SessaoService
    {
        public const string MotivoLimite = "limite_sessoes";
        public const string MotivoReuso = "reuso_detectado";
        public const string MotivoSubstituida = "substituida_por_novo_login";
        public const string MotivoEncerradaPeloUsuario = "encerrada_pelo_usuario";
        public const string MotivoLogoutTodas = "logout_todas";
        public const string MotivoContaInativa = "conta_inativa";
        public const string MotivoPapelAlterado = "papel_alterado";
        public const string MotivoSenhaAlterada = "senha_alterada";
        public const string MotivoSenhaRedefinida = "senha_redefinida";
        public const string MotivoMfaAlterado = "mfa_alterado";
        public const string MotivoContaExcluida = "conta_excluida";

        private readonly ISessaoStore _store;
        private readonly SessaoConfig _config;
        private readonly TimeProvider _relogio;
        private readonly IJwtService _jwt;
        private readonly IAuditLogRepository _audit;
        private readonly IUsuarioRepository _usuarios;
        private readonly IVerificacaoMFAService _mfa;
        private readonly IEmailService _email;
        private readonly IContaService _conta;
        private readonly IHostEnvironment _ambiente;

        public SessaoService(
            ISessaoStore store, SessaoConfig config, TimeProvider relogio, IJwtService jwt,
            IAuditLogRepository audit, IUsuarioRepository usuarios, IVerificacaoMFAService mfa,
            IEmailService email, IContaService conta, IHostEnvironment ambiente)
        {
            _store = store;
            _config = config;
            _relogio = relogio;
            _jwt = jwt;
            _audit = audit;
            _usuarios = usuarios;
            _mfa = mfa;
            _email = email;
            _conta = conta;
            _ambiente = ambiente;
        }

        // ── Criação ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Sessão NOVA (novo sid) a cada login/MFA — anti session fixation. A sessão que este
        /// navegador tinha antes é revogada; acima de MaxSessoes, a mais antiga também.
        /// </summary>
        public async Task<(DadosSessao Sessao, PoliticaSessao Politica)> CriarAsync(
            HttpContext http, UsuarioSessao usuario, bool manterConectado)
        {
            var ip = ClientRequestInfo.ObterIp(http);
            var politica = _config.Para(usuario.IsAdmin, manterConectado);
            var agora = _relogio.GetUtcNow();

            await RevogarSessaoAnteriorDoNavegadorAsync(http, ip);

            var vivas = await _store.ListarDoUsuarioAsync(usuario.Id);
            foreach (var (sidAntigo, _) in vivas.OrderBy(v => v.Sessao.CriadaEm).Take(Math.Max(0, vivas.Count - politica.MaxSessoes + 1)))
                await RevogarAsync(sidAntigo, usuario.Id, MotivoLimite, ip);

            var sid = NovoIdentificador(16);
            var segredo = NovoIdentificador(32);
            var sessao = new DadosSessao(
                usuario.Id, usuario.Papel, agora, agora + politica.DuracaoAbsoluta, agora,
                politica.ManterConectado, UserAgent(http), ip);

            await _store.CriarAsync(sid, sessao, SessaoAtual.TtlSessao(sessao, politica, agora), segredo, politica.DuracaoAbsoluta);
            await _audit.RegistrarAsync(usuario.Id, "sessao_criada", ip);

            EmitirCookies(http, usuario, sid, segredo, politica, sessao.ExpiraEm - agora);
            SessaoAtual.DefinirVerificada(http, sid, sessao);
            return (sessao, politica);
        }

        // ── Refresh ──────────────────────────────────────────────────────────────

        public async Task<ResultadoRefresh> RenovarAsync(HttpContext http)
        {
            if (string.IsNullOrEmpty(http.Request.Cookies[SessaoAtual.NomeCookieRefresh]))
                return new(StatusRefresh.SemSessao);
            if (!TentarLerCookieRefresh(http, out var sidCookie, out var segredo))
                return new(StatusRefresh.Desconhecido);

            var ip = ClientRequestInfo.ObterIp(http);
            var agora = _relogio.GetUtcNow();

            var sid = await _store.ObterSidDoRefreshAsync(segredo);
            if (sid is not null && sid == sidCookie)
            {
                var marca = new RefreshUsado(sid, agora);
                if (await _store.ConsumirRefreshAsync(segredo, sid, marca, _config.RetencaoRefreshUsado))
                    return await ConcluirRotacaoAsync(http, sid, ip, agora);
                // Perdeu a corrida: cai na análise da marca abaixo.
            }

            var usado = await _store.ObterRefreshUsadoAsync(segredo);
            if (usado is null)
                return new(StatusRefresh.Desconhecido);   // nunca visto (ou expirado): nada a revogar

            if (agora - usado.RotacionadoEm < _config.ToleranciaRefreshConcorrente)
                return new(StatusRefresh.Concorrente);   // duas abas, retry de rede: não é ataque

            // Token já rotacionado reapresentado depois da tolerância: alguém guardou uma cópia.
            // Revoga a sessão inteira (o dono legítimo também sai e precisa entrar de novo).
            var sessao = await _store.ObterAsync(usado.Sid);
            if (sessao is not null)
            {
                await _audit.RegistrarAsync(sessao.UsuarioId, "refresh_reuso_detectado", ip);
                await RevogarAsync(usado.Sid, sessao.UsuarioId, MotivoReuso, ip);
            }
            return new(StatusRefresh.ReusoDetectado);
        }

        private async Task<ResultadoRefresh> ConcluirRotacaoAsync(HttpContext http, string sid, string ip, DateTimeOffset agora)
        {
            var sessao = await _store.ObterAsync(sid);
            if (sessao is null)
                return new(StatusRefresh.SessaoEncerrada);

            if (agora >= sessao.ExpiraEm)
            {
                await _store.RemoverAsync(sid, sessao.UsuarioId);
                return new(StatusRefresh.SessaoEncerrada);
            }

            // O refresh relê a conta no banco: bloqueio, exclusão ou perda do papel Admin valem
            // em no máximo um access token (15 min; 10 min para Admin).
            var conta = await _usuarios.ObterPorIdAsync(sessao.UsuarioId);
            if (conta is null || conta.ExcluidoEm is not null || conta.Status != StatusUsuario.Active)
            {
                await RevogarTodasAsync(sessao.UsuarioId, MotivoContaInativa, ip);
                return new(StatusRefresh.SessaoEncerrada);
            }

            var usuario = UsuarioSessao.De(conta);
            if (usuario.Papel != sessao.Papel)
            {
                await RevogarAsync(sid, sessao.UsuarioId, MotivoPapelAlterado, ip);
                return new(StatusRefresh.SessaoEncerrada);
            }

            // NÃO conta como atividade: a sessão não é regravada, o TTL dela segue o mesmo.
            var politica = _config.Para(sessao.EhAdmin, sessao.ManterConectado);
            var segredo = NovoIdentificador(32);
            var restante = sessao.ExpiraEm - agora;
            await _store.GravarRefreshAsync(segredo, sid, restante);

            EmitirCookies(http, usuario, sid, segredo, politica, restante);
            return new(StatusRefresh.Renovado, sessao, politica);
        }

        // ── Atividade ────────────────────────────────────────────────────────────

        /// <summary>
        /// Renova a janela de inatividade se a última atividade registrada tem mais de
        /// <see cref="SessaoConfig.IntervaloRegistroAtividade"/>. <c>SET ... XX</c>: uma sessão
        /// revogada nesse meio-tempo não volta. Devolve a sessão como ficou.
        /// </summary>
        public static async Task<DadosSessao> RegistrarAtividadeAsync(
            ISessaoStore store, SessaoConfig config, string sid, DadosSessao sessao, DateTimeOffset agora)
        {
            if (agora - sessao.UltimaAtividadeEm <= config.IntervaloRegistroAtividade)
                return sessao;

            var politica = config.Para(sessao.EhAdmin, sessao.ManterConectado);
            var atualizada = sessao with { UltimaAtividadeEm = agora };
            var ttl = SessaoAtual.TtlSessao(atualizada, politica, agora);
            if (ttl <= TimeSpan.Zero)
                return sessao;

            return await store.AtualizarSeExisteAsync(sid, atualizada, ttl) ? atualizada : sessao;
        }

        // ── Revogação ────────────────────────────────────────────────────────────

        public async Task<bool> RevogarAsync(string sid, int usuarioId, string motivo, string ip)
        {
            var existia = await _store.RemoverAsync(sid, usuarioId);
            if (existia)
                await _audit.RegistrarAsync(usuarioId, $"sessao_revogada:{motivo}", ip);
            return existia;
        }

        /// <summary>Revoga todas as sessões do usuário, exceto <paramref name="preservarSid"/> (troca voluntária de senha).</summary>
        public async Task<int> RevogarTodasAsync(int usuarioId, string motivo, string ip, string? preservarSid = null)
        {
            var revogadas = 0;
            foreach (var (sid, _) in await _store.ListarDoUsuarioAsync(usuarioId))
            {
                if (sid == preservarSid) continue;
                if (await RevogarAsync(sid, usuarioId, motivo, ip)) revogadas++;
            }
            return revogadas;
        }

        /// <summary>Logout: encerra a sessão deste navegador (pelo access token ou, se ele já expirou, pelo refresh).</summary>
        public async Task EncerrarAtualAsync(HttpContext http)
        {
            var ip = ClientRequestInfo.ObterIp(http);
            var alvo = await SessaoDoNavegadorAsync(http);
            if (alvo is { } a && await _store.RemoverAsync(a.Sid, a.UsuarioId))
                await _audit.RegistrarAsync(a.UsuarioId, "sessao_encerrada", ip);

            LimparCookies(http);
        }

        public Task<IReadOnlyList<(string Sid, DadosSessao Sessao)>> ListarAsync(int usuarioId) =>
            _store.ListarDoUsuarioAsync(usuarioId);

        // ── Reautenticação ───────────────────────────────────────────────────────

        /// <summary>
        /// Senha (e, com MFA ativo ou para Admin, o código por e-mail reaproveitando
        /// <see cref="IVerificacaoMFAService"/>). Sucesso grava <c>reauth:{sid}</c> com TTL
        /// de ReautenticacaoMinutos.
        /// </summary>
        public async Task<ResultadoReautenticacao> ReautenticarAsync(HttpContext http, int usuarioId, string sid, string? senha, string? codigo)
        {
            var ip = ClientRequestInfo.ObterIp(http);
            var usuario = await _usuarios.ObterPorIdAsync(usuarioId)
                ?? throw new NegocioException("Usuário não encontrado");
            var exigeMfa = usuario.MFA || usuario.IsAdmin;

            if (!string.IsNullOrWhiteSpace(codigo))
            {
                if (!exigeMfa)
                    throw new NegocioException("Informe a senha.");
                try
                {
                    await _mfa.ValidarAsync(usuario.Email, codigo.Trim(), "reauth");
                }
                catch (NegocioException)
                {
                    await _audit.RegistrarAsync(usuarioId, "reauth_falha", ip);
                    throw;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(senha) || await _conta.ConferirSenhaAsync(usuarioId, senha) is null)
                {
                    await _audit.RegistrarAsync(usuarioId, "reauth_falha", ip);
                    throw new NegocioException("Senha incorreta.");
                }

                if (exigeMfa)
                {
                    var novoCodigo = await _mfa.GerarECriarAsync(usuario.Email, "reauth");
                    await _email.EnviarCodigoVerificacaoAsync(usuario.Email, novoCodigo);
                    return new ResultadoReautenticacao(RequerMFA: true);
                }
            }

            var politica = _config.Para(usuario.IsAdmin, manterConectado: false);
            await _store.MarcarReautenticacaoAsync(sid, politica.Reautenticacao);
            await _audit.RegistrarAsync(usuarioId, "reauth_ok", ip);
            return new ResultadoReautenticacao(RequerMFA: false);
        }

        // ── Cookies ──────────────────────────────────────────────────────────────

        public CookieOptions OpcoesCookieAccess(DateTimeOffset? expira) => new()
        {
            HttpOnly = true,
            Secure = !_ambiente.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expira
        };

        public CookieOptions OpcoesCookieRefresh(TimeSpan? maxAge) => new()
        {
            HttpOnly = true,
            Secure = !_ambiente.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = SessaoAtual.CaminhoCookieRefresh,
            MaxAge = maxAge
        };

        /// <summary>Apaga os dois cookies com os MESMOS atributos com que foram emitidos.</summary>
        public void LimparCookies(HttpContext http)
        {
            http.Response.Cookies.Delete(SessaoAtual.NomeCookieAccess, OpcoesCookieAccess(null));
            http.Response.Cookies.Delete(SessaoAtual.NomeCookieRefresh, OpcoesCookieRefresh(null));
        }

        /// <summary>Reemite só o access token (ex.: onboarding concluído muda o claim perfilCompleto).</summary>
        public void ReemitirAccess(HttpContext http, UsuarioSessao usuario, string sid, bool manterConectado)
        {
            var politica = _config.Para(usuario.IsAdmin, manterConectado);
            var token = _jwt.Gerar(usuario.Id, usuario.Email, usuario.Nome, usuario.Tipo, usuario.PerfilCompleto, usuario.IsAdmin, sid, politica.AccessToken);
            http.Response.Cookies.Append(SessaoAtual.NomeCookieAccess, token, OpcoesCookieAccess(_relogio.GetUtcNow() + politica.AccessToken));
        }

        private void EmitirCookies(HttpContext http, UsuarioSessao usuario, string sid, string segredo, PoliticaSessao politica, TimeSpan validadeRefresh)
        {
            var token = _jwt.Gerar(usuario.Id, usuario.Email, usuario.Nome, usuario.Tipo, usuario.PerfilCompleto, usuario.IsAdmin, sid, politica.AccessToken);
            http.Response.Cookies.Append(SessaoAtual.NomeCookieAccess, token, OpcoesCookieAccess(_relogio.GetUtcNow() + politica.AccessToken));
            http.Response.Cookies.Append(SessaoAtual.NomeCookieRefresh, $"{sid}.{segredo}", OpcoesCookieRefresh(validadeRefresh));
        }

        // ── Auxiliares ───────────────────────────────────────────────────────────

        private async Task RevogarSessaoAnteriorDoNavegadorAsync(HttpContext http, string ip)
        {
            if (await SessaoDoNavegadorAsync(http) is { } anterior)
                await RevogarAsync(anterior.Sid, anterior.UsuarioId, MotivoSubstituida, ip);
        }

        /// <summary>
        /// A sessão deste navegador, só por fontes confiáveis: o access token assinado já
        /// conferido, ou o refresh token resolvido no armazenamento (o sid escrito no cookie,
        /// sozinho, não vale nada).
        /// </summary>
        private async Task<(string Sid, int UsuarioId)?> SessaoDoNavegadorAsync(HttpContext http)
        {
            var sid = SessaoAtual.Sid(http);
            var usuarioId = SessaoAtual.UsuarioIdDoToken(http.User);
            if (sid is not null && usuarioId is not null)
                return (sid, usuarioId.Value);

            if (!TentarLerCookieRefresh(http, out var sidCookie, out var segredo))
                return null;

            var sidReal = await _store.ObterSidDoRefreshAsync(segredo);
            if (sidReal is null || sidReal != sidCookie)
                return null;

            var sessao = await _store.ObterAsync(sidReal);
            return sessao is null ? null : (sidReal, sessao.UsuarioId);
        }

        private static bool TentarLerCookieRefresh(HttpContext http, out string sid, out string segredo)
        {
            sid = segredo = string.Empty;
            var valor = http.Request.Cookies[SessaoAtual.NomeCookieRefresh];
            if (string.IsNullOrEmpty(valor) || valor.Length > 128)
                return false;

            var partes = valor.Split('.');
            if (partes.Length != 2 || partes[0].Length == 0 || partes[1].Length < 40)
                return false;

            sid = partes[0];
            segredo = partes[1];
            return true;
        }

        private static string UserAgent(HttpContext http)
        {
            var ua = new string(http.Request.Headers.UserAgent.ToString().Where(c => !char.IsControl(c)).ToArray());
            return ua.Length <= DadosSessao.TamanhoMaximoUserAgent ? ua : ua[..DadosSessao.TamanhoMaximoUserAgent];
        }

        /// <summary>Aleatório criptográfico em base64url (16 bytes = sid; 32 bytes = 256 bits do refresh).</summary>
        private static string NovoIdentificador(int bytes) =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
