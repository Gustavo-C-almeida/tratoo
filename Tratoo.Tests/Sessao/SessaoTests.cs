using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests.Sessao
{
    /// <summary>
    /// Controle de sessão com o host em memória (sem Docker): expiração por inatividade e
    /// absoluta pelo relógio de teste, rotação e reuso do refresh, limite de sessões,
    /// reautenticação, revogação em massa, cookies e tokens antigos sem sid. Os mesmos
    /// pontos com Redis real e duas réplicas estão em Integracao/SessaoRedisTests.
    /// </summary>
    public class SessaoTests
    {
        private static Task<HostDeSessao> SubirAsync(RelogioDeTeste? relogio = null, Dictionary<string, string?>? config = null) =>
            HostDeSessao.SubirAsync(config, relogio);

        // ── Login e cookies ─────────────────────────────────────────────────────

        [Fact]
        public async Task Login_emite_os_dois_cookies_com_os_atributos_esperados()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();

            await host.EntrarAsync(nav, usuario);

            var access = nav.SetCookies.Single(c => c.StartsWith("tratoo_auth=", StringComparison.Ordinal)).ToLowerInvariant();
            Assert.Contains("httponly", access);
            Assert.Contains("secure", access);
            Assert.Contains("samesite=strict", access);
            Assert.Contains("path=/;", access + ";");

            var refresh = nav.SetCookies.Single(c => c.StartsWith("tratoo_refresh=", StringComparison.Ordinal)).ToLowerInvariant();
            Assert.Contains("httponly", refresh);
            Assert.Contains("secure", refresh);
            Assert.Contains("samesite=strict", refresh);
            Assert.Contains("path=/api/auth", refresh);
            Assert.Contains("max-age=43200", refresh);   // 12 h, a duração absoluta

            // {sid}.{segredo}: segredo de 256 bits em base64url = 43 caracteres.
            var partes = nav.Cookie(SessaoAtual.NomeCookieRefresh)!.Split('.');
            Assert.Equal(2, partes.Length);
            Assert.Equal(43, partes[1].Length);

            // Access token de 15 min com o claim sid.
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(nav.Cookie(SessaoAtual.NomeCookieAccess));
            Assert.Equal(partes[0], jwt.Claims.Single(c => c.Type == "sid").Value);
            Assert.InRange(jwt.ValidTo - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));

            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, "sessao_criada"));
        }

        [Fact]
        public async Task Manter_conectado_estende_o_refresh_para_14_dias()
        {
            await using var host = await SubirAsync();
            var nav = host.NovoNavegador();

            await host.EntrarAsync(nav, host.Usuarios.Novo(), manterConectado: true);

            var refresh = nav.SetCookies.Single(c => c.StartsWith("tratoo_refresh=", StringComparison.Ordinal)).ToLowerInvariant();
            Assert.Contains("max-age=1209600", refresh);
        }

        [Fact]
        public async Task Admin_sempre_passa_pelo_MFA_e_recebe_access_de_10_minutos()
        {
            await using var host = await SubirAsync();
            var admin = host.Usuarios.Novo(mfa: false, admin: true);
            var nav = host.NovoNavegador();

            var primeiro = await nav.PostAsync("/usuarios/login", new { email = admin.Email, senha = HostDeSessao.Senha });
            var corpo = await primeiro.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
            Assert.Equal("True", corpo!["requerMFA"]!.ToString(), ignoreCase: true);
            Assert.Null(nav.Cookie(SessaoAtual.NomeCookieAccess));   // nada de sessão antes do código

            await host.EntrarAsync(nav, admin);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(nav.Cookie(SessaoAtual.NomeCookieAccess));
            Assert.InRange(jwt.ValidTo - DateTime.UtcNow, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10.1));
            Assert.Contains("max-age=28800", nav.SetCookies.Last(c => c.StartsWith("tratoo_refresh=", StringComparison.Ordinal)).ToLowerInvariant());
        }

        [Fact]
        public async Task Novo_login_no_mesmo_navegador_cria_outro_sid_e_revoga_o_anterior()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();

            await host.EntrarAsync(nav, usuario);
            var sidAntigo = nav.Sid;
            await host.EntrarAsync(nav, usuario);

            Assert.NotEqual(sidAntigo, nav.Sid);
            var sessoes = await host.Store.ListarDoUsuarioAsync(usuario.Id);
            Assert.Single(sessoes);
            Assert.Equal(nav.Sid, sessoes[0].Sid);
            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, $"sessao_revogada:{SessaoService.MotivoSubstituida}"));
        }

        [Fact]
        public async Task Me_devolve_as_datas_da_sessao()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());

            var me = await nav.GetAsync("/api/me");
            var corpo = await me.Content.ReadFromJsonAsync<RespostaMe>();

            Assert.NotNull(corpo!.Sessao);
            Assert.Equal(relogio.GetUtcNow() + TimeSpan.FromMinutes(30), corpo.Sessao!.ExpiraPorInatividadeEm);
            Assert.Equal(relogio.GetUtcNow() + TimeSpan.FromHours(12), corpo.Sessao.ExpiraAbsolutaEm);
            Assert.Equal(1800, corpo.Sessao.JanelaInatividadeSegundos);
        }

        // ── Validação a cada requisição ─────────────────────────────────────────

        [Fact]
        public async Task Token_antigo_sem_sid_responde_401_SESSAO_ENCERRADA()
        {
            await using var host = await SubirAsync();
            var nav = host.NovoNavegador();
            nav.DefinirCookie(SessaoAtual.NomeCookieAccess, TokenSemSid());

            var r = await nav.GetAsync("/teste/comum");

            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal(SessaoAutenticacaoSetup.CodigoSessaoEncerrada, await Respostas.CodigoErroAsync(r));
        }

        [Fact]
        public async Task Sessao_expira_por_inatividade_mesmo_com_o_JWT_ainda_valido()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());

            relogio.Avancar(TimeSpan.FromMinutes(31));
            var r = await nav.GetAsync("/teste/comum");

            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal(SessaoAutenticacaoSetup.CodigoSessaoEncerrada, await Respostas.CodigoErroAsync(r));
        }

        [Fact]
        public async Task Atividade_real_desliza_a_janela_de_inatividade()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());

            relogio.Avancar(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum")).StatusCode);

            // 40 min desde o login, 20 desde a última atividade.
            relogio.Avancar(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum")).StatusCode);
        }

        [Fact]
        public async Task Requisicao_passiva_nao_renova_a_sessao()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());

            relogio.Avancar(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum", passivo: true)).StatusCode);

            relogio.Avancar(TimeSpan.FromMinutes(11));
            Assert.Equal(HttpStatusCode.Unauthorized, (await nav.GetAsync("/teste/comum", passivo: true)).StatusCode);
        }

        [Fact]
        public async Task Atividade_dentro_de_60_segundos_nao_regrava_a_sessao()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());
            var criada = (await host.Store.ObterAsync(nav.Sid!))!.UltimaAtividadeEm;

            relogio.Avancar(TimeSpan.FromSeconds(50));
            await nav.GetAsync("/teste/comum");
            Assert.Equal(criada, (await host.Store.ObterAsync(nav.Sid!))!.UltimaAtividadeEm);

            relogio.Avancar(TimeSpan.FromSeconds(20));
            await nav.GetAsync("/teste/comum");
            Assert.Equal(relogio.GetUtcNow(), (await host.Store.ObterAsync(nav.Sid!))!.UltimaAtividadeEm);
        }

        [Fact]
        public async Task Sessao_expira_na_duracao_absoluta_mesmo_com_atividade_constante()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());

            // Atividade a cada 20 min durante 11h40: a sessão segue viva.
            for (var i = 0; i < 35; i++)
            {
                relogio.Avancar(TimeSpan.FromMinutes(20));
                Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum")).StatusCode);
            }

            relogio.Avancar(TimeSpan.FromMinutes(20));   // 12h
            Assert.Equal(HttpStatusCode.Unauthorized, (await nav.GetAsync("/teste/comum")).StatusCode);
        }

        // ── Refresh ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Refresh_rotaciona_o_token_e_emite_access_novo()
        {
            await using var host = await SubirAsync();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());
            var refreshAntigo = nav.Cookie(SessaoAtual.NomeCookieRefresh);
            var sid = nav.Sid;

            var r = await nav.PostAsync("/api/auth/refresh");

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.NotEqual(refreshAntigo, nav.Cookie(SessaoAtual.NomeCookieRefresh));
            Assert.Equal(sid, nav.Sid);   // mesma sessão, token novo
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum")).StatusCode);
        }

        [Fact]
        public async Task Refresh_nao_conta_como_atividade()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());

            relogio.Avancar(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/api/auth/refresh")).StatusCode);

            relogio.Avancar(TimeSpan.FromMinutes(11));   // 31 min desde a última atividade real
            Assert.Equal(HttpStatusCode.Unauthorized, (await nav.GetAsync("/teste/comum")).StatusCode);
        }

        [Fact]
        public async Task Token_reapresentado_dentro_da_tolerancia_responde_409_e_nao_revoga()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);
            var antigo = nav.Cookie(SessaoAtual.NomeCookieRefresh)!;

            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/api/auth/refresh")).StatusCode);

            // Outra aba que ainda tinha o cookie antigo.
            var outraAba = host.NovoNavegador();
            outraAba.DefinirCookie(SessaoAtual.NomeCookieRefresh, antigo, SessaoAtual.CaminhoCookieRefresh);
            relogio.Avancar(TimeSpan.FromSeconds(5));
            var r = await outraAba.PostAsync("/api/auth/refresh");

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal(Tratoo.API.EndPoints.AuthExtensions.CodigoRefreshConcorrente, await Respostas.CodigoErroAsync(r));
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/api/auth/refresh")).StatusCode);
            Assert.Equal(0, host.Auditoria.Contar(usuario.Id, "refresh_reuso_detectado"));
        }

        [Fact]
        public async Task Token_reapresentado_fora_da_tolerancia_revoga_a_sessao()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);
            var antigo = nav.Cookie(SessaoAtual.NomeCookieRefresh)!;
            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/api/auth/refresh")).StatusCode);

            var atacante = host.NovoNavegador();
            atacante.DefinirCookie(SessaoAtual.NomeCookieRefresh, antigo, SessaoAtual.CaminhoCookieRefresh);
            relogio.Avancar(TimeSpan.FromSeconds(11));
            var r = await atacante.PostAsync("/api/auth/refresh");

            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            // O dono legítimo também perde a sessão, na próxima requisição.
            Assert.Equal(HttpStatusCode.Unauthorized, (await nav.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await nav.PostAsync("/api/auth/refresh")).StatusCode);
            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, "refresh_reuso_detectado"));
            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, $"sessao_revogada:{SessaoService.MotivoReuso}"));
        }

        [Fact]
        public async Task Token_desconhecido_responde_401_sem_revogar_nada()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);

            var estranho = host.NovoNavegador();
            estranho.DefinirCookie(SessaoAtual.NomeCookieRefresh, $"{nav.Sid}.{new string('A', 43)}", SessaoAtual.CaminhoCookieRefresh);
            var r = await estranho.PostAsync("/api/auth/refresh");

            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum")).StatusCode);
            Assert.DoesNotContain(host.Auditoria.Registros, x => x.Acao.StartsWith("sessao_revogada", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Refresh_sem_cookie_responde_401_SESSAO_AUSENTE()
        {
            await using var host = await SubirAsync();
            var r = await host.NovoNavegador().PostAsync("/api/auth/refresh");

            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal(Tratoo.API.EndPoints.AuthExtensions.CodigoSessaoAusente, await Respostas.CodigoErroAsync(r));
        }

        [Fact]
        public async Task Refreshes_simultaneos_com_o_mesmo_token_tem_um_unico_vencedor()
        {
            await using var host = await SubirAsync();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());
            var token = nav.Cookie(SessaoAtual.NomeCookieRefresh)!;

            var abas = Enumerable.Range(0, 10).Select(_ =>
            {
                var aba = host.NovoNavegador();
                aba.DefinirCookie(SessaoAtual.NomeCookieRefresh, token, SessaoAtual.CaminhoCookieRefresh);
                return aba;
            }).ToList();

            var respostas = await Task.WhenAll(abas.Select(a => a.PostAsync("/api/auth/refresh")));

            Assert.Equal(1, respostas.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(9, respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.NotNull(await host.Store.ObterAsync(nav.Sid!));   // nada revogado
        }

        [Fact]
        public async Task Refresh_de_conta_bloqueada_encerra_a_sessao()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);

            usuario.Status = Tratoo.Domain.Enums.StatusUsuario.Blocked;
            var r = await nav.PostAsync("/api/auth/refresh");

            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Empty(await host.Store.ListarDoUsuarioAsync(usuario.Id));
        }

        // ── Limite de sessões ───────────────────────────────────────────────────

        [Fact]
        public async Task Acima_do_limite_a_sessao_mais_antiga_e_revogada()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio, new() { ["Sessao:MaxSessoes"] = "2" });
            var usuario = host.Usuarios.Novo();
            var navs = new[] { host.NovoNavegador("A"), host.NovoNavegador("B"), host.NovoNavegador("C") };

            foreach (var nav in navs)
            {
                await host.EntrarAsync(nav, usuario);
                relogio.Avancar(TimeSpan.FromSeconds(1));
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await navs[0].GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await navs[1].GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await navs[2].GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, $"sessao_revogada:{SessaoService.MotivoLimite}"));
        }

        // ── Reautenticação ──────────────────────────────────────────────────────

        [Fact]
        public async Task Acao_sensivel_sem_reautenticacao_responde_403_REAUTH_REQUIRED()
        {
            var relogio = new RelogioDeTeste();
            await using var host = await SubirAsync(relogio);
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);

            var r = await nav.PostAsync("/teste/reauth");
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal(SessaoAutenticacaoSetup.CodigoReautenticacao, await Respostas.CodigoErroAsync(r));

            Assert.Equal(HttpStatusCode.BadRequest, (await nav.PostAsync("/api/auth/reautenticar", new { senha = "errada" })).StatusCode);
            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, "reauth_falha"));

            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/api/auth/reautenticar", new { senha = HostDeSessao.Senha })).StatusCode);
            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, "reauth_ok"));
            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/teste/reauth")).StatusCode);

            // A marca dura ReautenticacaoMinutos (10).
            relogio.Avancar(TimeSpan.FromMinutes(11));
            Assert.Equal(HttpStatusCode.Forbidden, (await nav.PostAsync("/teste/reauth")).StatusCode);
        }

        [Fact]
        public async Task Reautenticacao_com_MFA_exige_o_codigo_depois_da_senha()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo(mfa: true);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);

            var passo1 = await nav.PostAsync("/api/auth/reautenticar", new { senha = HostDeSessao.Senha });
            var corpo = await passo1.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
            Assert.Equal("True", corpo!["requerMFA"]!.ToString(), ignoreCase: true);
            Assert.Equal(HttpStatusCode.Forbidden, (await nav.PostAsync("/teste/reauth")).StatusCode);

            var codigo = host.Emails.UltimoCodigo(usuario.Email);
            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/api/auth/reautenticar", new { codigo })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/teste/reauth")).StatusCode);
        }

        [Fact]
        public async Task Reautenticacao_vale_so_para_a_sessao_em_que_foi_feita()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var a = host.NovoNavegador("A");
            var b = host.NovoNavegador("B");
            await host.EntrarAsync(a, usuario);
            await host.EntrarAsync(b, usuario);

            await a.PostAsync("/api/auth/reautenticar", new { senha = HostDeSessao.Senha });

            Assert.Equal(HttpStatusCode.OK, (await a.PostAsync("/teste/reauth")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsync("/teste/reauth")).StatusCode);
        }

        // ── Revogação ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Logout_revoga_na_hora_e_apaga_os_cookies_com_os_mesmos_atributos()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);
            var access = nav.Cookie(SessaoAtual.NomeCookieAccess)!;
            nav.SetCookies.Clear();

            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/api/auth/logout")).StatusCode);

            var apagaAccess = nav.SetCookies.Single(c => c.StartsWith("tratoo_auth=;", StringComparison.Ordinal)).ToLowerInvariant();
            var apagaRefresh = nav.SetCookies.Single(c => c.StartsWith("tratoo_refresh=;", StringComparison.Ordinal)).ToLowerInvariant();
            foreach (var c in new[] { apagaAccess, apagaRefresh })
            {
                Assert.Contains("expires=thu, 01 jan 1970", c);
                Assert.Contains("httponly", c);
                Assert.Contains("secure", c);
                Assert.Contains("samesite=strict", c);
            }
            Assert.Contains("path=/api/auth", apagaRefresh);

            // O access token ainda não expirou, mas a sessão dele não existe mais.
            var copia = host.NovoNavegador();
            copia.DefinirCookie(SessaoAtual.NomeCookieAccess, access);
            Assert.Equal(HttpStatusCode.Unauthorized, (await copia.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, "sessao_encerrada"));
        }

        [Fact]
        public async Task Logout_com_access_expirado_encerra_pela_sessao_do_refresh()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);
            var sid = nav.Sid!;

            var semAccess = host.NovoNavegador();
            semAccess.DefinirCookie(SessaoAtual.NomeCookieRefresh, nav.Cookie(SessaoAtual.NomeCookieRefresh)!, SessaoAtual.CaminhoCookieRefresh);
            await semAccess.PostAsync("/api/auth/logout");

            Assert.Null(await host.Store.ObterAsync(sid));
        }

        [Fact]
        public async Task Logout_todas_derruba_todos_os_dispositivos()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var a = host.NovoNavegador("A");
            var b = host.NovoNavegador("B");
            await host.EntrarAsync(a, usuario);
            await host.EntrarAsync(b, usuario);

            Assert.Equal(HttpStatusCode.OK, (await b.PostAsync("/api/auth/logout-todas")).StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/teste/comum")).StatusCode);
            Assert.Empty(await host.Store.ListarDoUsuarioAsync(usuario.Id));
        }

        [Fact]
        public async Task Troca_de_senha_revoga_as_outras_sessoes_e_preserva_a_atual()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var a = host.NovoNavegador("A");
            var b = host.NovoNavegador("B");
            await host.EntrarAsync(a, usuario);
            await host.EntrarAsync(b, usuario);

            Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsync("/api/conta/senha", new { novaSenha = "Outra@Senha2", confirmarNovaSenha = "Outra@Senha2" })).StatusCode);
            await b.PostAsync("/api/auth/reautenticar", new { senha = HostDeSessao.Senha });
            var r = await b.PostAsync("/api/conta/senha", new { novaSenha = "Outra@Senha2", confirmarNovaSenha = "Outra@Senha2" });

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(1, host.Auditoria.Contar(usuario.Id, $"sessao_revogada:{SessaoService.MotivoSenhaAlterada}"));
        }

        [Fact]
        public async Task Ativar_MFA_revoga_todas_as_sessoes_inclusive_a_atual()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);
            await nav.PostAsync("/api/auth/reautenticar", new { senha = HostDeSessao.Senha });
            var sid = nav.Sid!;

            Assert.Equal(HttpStatusCode.OK, (await nav.PutAsync("/api/conta/mfa", new { ativo = true })).StatusCode);

            Assert.True(usuario.MFA);
            Assert.Null(await host.Store.ObterAsync(sid));
            Assert.Null(nav.Cookie(SessaoAtual.NomeCookieAccess));
        }

        [Fact]
        public async Task Admin_nao_pode_desligar_o_MFA()
        {
            await using var host = await SubirAsync();
            var admin = host.Usuarios.Novo(mfa: true, admin: true);
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, admin);
            await nav.PostAsync("/api/auth/reautenticar", new { senha = HostDeSessao.Senha });
            await nav.PostAsync("/api/auth/reautenticar", new { codigo = host.Emails.UltimoCodigo(admin.Email) });

            Assert.Equal(HttpStatusCode.BadRequest, (await nav.PutAsync("/api/conta/mfa", new { ativo = false })).StatusCode);
            Assert.True(admin.MFA);
        }

        [Fact]
        public async Task Redefinicao_de_senha_revoga_todas_as_sessoes()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, usuario);

            var anonimo = host.NovoNavegador();
            await anonimo.PostAsync("/usuarios/senha/resetar/solicitar", new { email = usuario.Email });
            var r = await anonimo.PostAsync("/usuarios/senha/resetar", new
            {
                email = usuario.Email,
                codigo = host.Emails.UltimoCodigo(usuario.Email),
                novaSenha = "Nova@Senha3",
                confirmarNovaSenha = "Nova@Senha3"
            });

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await nav.GetAsync("/teste/comum")).StatusCode);
        }

        [Fact]
        public async Task Exclusao_de_conta_exige_reautenticacao_e_revoga_todas_as_sessoes()
        {
            await using var host = await SubirAsync();
            var usuario = host.Usuarios.Novo();
            var a = host.NovoNavegador("A");
            var b = host.NovoNavegador("B");
            await host.EntrarAsync(a, usuario);
            await host.EntrarAsync(b, usuario);

            Assert.Equal(HttpStatusCode.Forbidden, (await a.DeleteAsync("/usuarios/conta")).StatusCode);
            await a.PostAsync("/api/auth/reautenticar", new { senha = HostDeSessao.Senha });
            Assert.Equal(HttpStatusCode.OK, (await a.DeleteAsync("/usuarios/conta")).StatusCode);

            Assert.Empty(await host.Store.ListarDoUsuarioAsync(usuario.Id));
            Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/teste/comum")).StatusCode);
        }

        // ── Dispositivos conectados ─────────────────────────────────────────────

        [Fact]
        public async Task Lista_de_sessoes_marca_a_atual_e_nao_encerra_sessao_de_outro_usuario()
        {
            await using var host = await SubirAsync();
            var dono = host.Usuarios.Novo();
            var outro = host.Usuarios.Novo();
            var a = host.NovoNavegador("Navegador A");
            var b = host.NovoNavegador("Navegador B");
            var intruso = host.NovoNavegador("Intruso");
            await host.EntrarAsync(a, dono);
            await host.EntrarAsync(b, dono);
            await host.EntrarAsync(intruso, outro);

            var lista = await (await a.GetAsync("/api/auth/sessoes")).Content.ReadFromJsonAsync<List<ItemSessao>>();
            Assert.Equal(2, lista!.Count);
            Assert.True(lista.Single(s => s.Id == a.Sid).Atual);
            Assert.False(lista.Single(s => s.Id == b.Sid).Atual);
            Assert.Equal("Navegador B", lista.Single(s => s.Id == b.Sid).Dispositivo);

            Assert.Equal(HttpStatusCode.NotFound, (await intruso.DeleteAsync($"/api/auth/sessoes/{a.Sid}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/teste/comum")).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await a.DeleteAsync($"/api/auth/sessoes/{b.Sid}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/teste/comum")).StatusCode);
        }

        // ── Armazenamento indisponível ──────────────────────────────────────────

        [Fact]
        public async Task Sem_armazenamento_rota_sensivel_responde_503_e_rota_comum_aceita_o_JWT()
        {
            await using var host = await SubirAsync();
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());

            host.Store.Indisponivel = true;

            var sensivel = await nav.GetAsync("/teste/sensivel");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, sensivel.StatusCode);
            Assert.Equal("5", sensivel.Headers.RetryAfter?.ToString());
            Assert.Equal(SessaoAutenticacaoSetup.CodigoSessaoIndisponivel, await Respostas.CodigoErroAsync(sensivel));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await nav.PostAsync("/teste/reauth")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await nav.PostAsync("/api/auth/refresh")).StatusCode);

            var comum = await nav.GetAsync("/teste/comum");
            Assert.Equal(HttpStatusCode.OK, comum.StatusCode);
            var corpo = await comum.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
            Assert.Equal(nameof(EstadoVerificacaoSessao.ArmazenamentoIndisponivel), corpo!["estado"]!.ToString());

            // Volta o Redis: tudo normal de novo, sem novo login.
            host.Store.Indisponivel = false;
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/sensivel")).StatusCode);
        }

        // ── Auxiliares ──────────────────────────────────────────────────────────

        private static string TokenSemSid()
        {
            var cfg = HostDeSessao.ConfigBase();
            var credenciais = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:SecretKey"]!)), SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: cfg["Jwt:Issuer"], audience: cfg["Jwt:Audience"],
                claims: new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, "7"),
                    new Claim(ClaimTypes.Role, "Contratante"),
                    new Claim("perfilCompleto", "true")
                },
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: credenciais);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private sealed record RespostaMe(SessaoMe? Sessao);
        private sealed record SessaoMe(DateTimeOffset ExpiraPorInatividadeEm, DateTimeOffset ExpiraAbsolutaEm, long JanelaInatividadeSegundos);
        private sealed record ItemSessao(string Id, bool Atual, string Dispositivo);
    }

    public class SessaoConfigTests
    {
        private static SessaoConfig Ler(Dictionary<string, string?> valores) =>
            SessaoConfig.DeConfiguracao(new ConfigurationBuilder().AddInMemoryCollection(valores).Build());

        [Fact]
        public void Sem_configuracao_valem_os_padroes_do_codigo()
        {
            var c = Ler(new());
            Assert.Equal(TimeSpan.FromMinutes(15), c.AccessToken);
            Assert.Equal(TimeSpan.FromMinutes(10), c.AccessTokenAdmin);
            Assert.Equal(TimeSpan.FromMinutes(30), c.Inatividade);
            Assert.Equal(TimeSpan.FromMinutes(15), c.InatividadeAdmin);
            Assert.Equal(TimeSpan.FromHours(12), c.DuracaoAbsoluta);
            Assert.Equal(TimeSpan.FromHours(8), c.DuracaoAbsolutaAdmin);
            Assert.Equal(TimeSpan.FromDays(14), c.ManterConectado);
            Assert.Equal(TimeSpan.FromDays(7), c.InatividadeManterConectado);
            Assert.Equal(5, c.MaxSessoes);
            Assert.Equal(2, c.MaxSessoesAdmin);
            Assert.Equal(TimeSpan.FromMinutes(10), c.Reautenticacao);
            Assert.Equal(TimeSpan.FromMinutes(5), c.ReautenticacaoAdmin);
            Assert.Equal(TimeSpan.FromSeconds(10), c.ToleranciaRefreshConcorrente);
        }

        [Fact]
        public void Valores_fracionarios_permitem_tempos_em_segundos()
        {
            var c = Ler(new() { ["Sessao:InatividadeMinutos"] = "0.05", ["Sessao:AccessTokenMinutos"] = "0.5" });
            Assert.Equal(TimeSpan.FromSeconds(3), c.Inatividade);
            Assert.Equal(TimeSpan.FromSeconds(30), c.AccessToken);
        }

        [Theory]
        [InlineData("Sessao:InatividadeMinutos", "0")]
        [InlineData("Sessao:InatividadeMinutos", "abc")]
        [InlineData("Sessao:MaxSessoes", "0")]
        public void Valor_invalido_falha_na_subida(string chave, string valor)
        {
            Assert.Throws<InvalidOperationException>(() => Ler(new() { [chave] = valor }));
        }

        [Fact]
        public void Manter_conectado_nao_vale_para_Admin()
        {
            var c = new SessaoConfig();
            var admin = c.Para(admin: true, manterConectado: true);
            Assert.False(admin.ManterConectado);
            Assert.Equal(TimeSpan.FromHours(8), admin.DuracaoAbsoluta);

            var comum = c.Para(admin: false, manterConectado: true);
            Assert.Equal(TimeSpan.FromDays(14), comum.DuracaoAbsoluta);
            Assert.Equal(TimeSpan.FromDays(7), comum.Inatividade);
        }

        [Fact]
        public void TTL_da_sessao_nunca_passa_da_expiracao_absoluta()
        {
            var agora = DateTimeOffset.UtcNow;
            var politica = new SessaoConfig().Para(false, false);
            var sessao = new DadosSessao(1, "Contratante", agora.AddHours(-12).AddMinutes(10), agora.AddMinutes(10), agora, false, "", "");

            Assert.Equal(TimeSpan.FromMinutes(10), SessaoAtual.TtlSessao(sessao, politica, agora));
            Assert.Equal(agora.AddMinutes(10), SessaoAtual.Expiracoes(sessao, politica).PorInatividade);
        }
    }
}
