using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Enums;
using Xunit;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// Fase 2 da auditoria — inventário de rotas e autenticação, contra a API real.
    /// Orientado pela <see cref="MatrizEndpoints"/>: rota nova sem guarda (ou fora da matriz)
    /// quebra esta suíte.
    /// </summary>
    [Collection(ColecaoApiReal.Nome)]
    public sealed class AutenticacaoTests
    {
        private readonly ApiRealFixture _f;
        public AutenticacaoTests(ApiRealFixture f) => _f = f;

        // ─────────────────────────────────────────────────────────────────────
        // Inventário
        // ─────────────────────────────────────────────────────────────────────

        [FactApiReal]
        public async Task Inventario_RotasDaApiReal_SaoExatamenteAsDaMatriz()
        {
            // Swagger só existe em Development; a lista de endpoints é a mesma (as rotas de
            // seed entram, os health checks não aparecem no ApiExplorer).
            var json = await _f.ApiDev.Http.GetStringAsync("/swagger/v1/swagger.json");
            using var doc = JsonDocument.Parse(json);
            var naApi = doc.RootElement.GetProperty("paths").EnumerateObject()
                .SelectMany(p => p.Value.EnumerateObject().Select(m => $"{m.Name.ToUpperInvariant()} {p.Name}"))
                .ToHashSet();

            var naMatriz = MatrizEndpoints.Todas
                .Where(r => !r.Modelo.StartsWith("/health/", StringComparison.Ordinal))
                .Select(r => $"{r.Metodo} {r.ModeloSwagger}")
                .ToHashSet();

            var semGuardaDecidida = naApi.Except(naMatriz).OrderBy(x => x).ToList();
            var inexistentes = naMatriz.Except(naApi).OrderBy(x => x).ToList();

            Assert.True(semGuardaDecidida.Count == 0,
                "Rotas na API que não estão na matriz de segurança (decida a guarda e inclua em MatrizEndpoints):\n" +
                string.Join("\n", semGuardaDecidida));
            Assert.True(inexistentes.Count == 0, "Rotas na matriz que a API não tem mais:\n" + string.Join("\n", inexistentes));
        }

        [FactApiReal]
        public async Task Inventario_HealthChecks_ExistemEmProducao()
        {
            foreach (var rota in new[] { "/health/live", "/health/ready" })
            {
                using var r = await _f.Api.EnviarAsync(HttpMethod.Get, rota);
                Assert.NotEqual(HttpStatusCode.NotFound, r.StatusCode);
            }
        }

        [FactApiReal]
        public async Task RotasDeSeed_NaoExistemEmProducao()
        {
            using var admin = await _f.Api.EnviarAsync(HttpMethod.Post, $"/api/dev/seed-admin?email={_f.Dados.ContratanteB.Email}");
            using var pagamentos = await _f.Api.EnviarAsync(HttpMethod.Post, "/api/dev/seed-pagamentos");

            Assert.Equal(HttpStatusCode.NotFound, admin.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, pagamentos.StatusCode);
            await using var db = _f.Db();
            Assert.False((await db.Usuarios.SingleAsync(u => u.Id == _f.Dados.ContratanteB.Id)).IsAdmin);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Rotas protegidas: nenhuma credencial inválida passa
        // ─────────────────────────────────────────────────────────────────────

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.Protegidas), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaProtegida_SemToken_Responde401(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            using var r = await _f.Api.EnviarAsync(rota.Http, rota.Url());
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.Protegidas), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaProtegida_TokenInvalido_Responde401(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            var sessao = await _f.SessaoAsync(_f.Dados.ContratanteVarredura);
            var u = sessao.Usuario;
            var agora = DateTime.UtcNow;

            var tokens = new Dictionary<string, string>
            {
                ["malformado"] = "isto.nao.e-um-jwt",
                ["lixo"] = "x",
                ["assinatura com outra chave"] = ApiRealFixture.ForjarToken(u, sessao.Sid, "outra-chave-qualquer-de-32-bytes-ou-mais-000", agora.AddMinutes(10)),
                ["expirado (chave e sessão válidas)"] = ApiRealFixture.ForjarToken(u, sessao.Sid, ApiRealFixture.ChaveJwt, agora.AddMinutes(-1), agora.AddMinutes(-20)),
                ["ainda não vigente (nbf futuro)"] = ApiRealFixture.ForjarToken(u, sessao.Sid, ApiRealFixture.ChaveJwt, agora.AddMinutes(30), agora.AddMinutes(10)),
                ["sem sid (chave válida)"] = ApiRealFixture.ForjarToken(u, null, ApiRealFixture.ChaveJwt, agora.AddMinutes(10)),
                ["sid inexistente (chave válida)"] = ApiRealFixture.ForjarToken(u, "sessao-que-nao-existe", ApiRealFixture.ChaveJwt, agora.AddMinutes(10)),
                ["emissor/audiência errados"] = ApiRealFixture.ForjarToken(u, sessao.Sid, ApiRealFixture.ChaveJwt, agora.AddMinutes(10), emissor: "outro-emissor"),
                ["alg none"] = ApiRealFixture.TokenSemAssinatura(sessao.Token),
            };

            var falhas = new List<string>();
            foreach (var (nome, token) in tokens)
            {
                // Pelo cookie (navegador) e pelo header Authorization (o JwtBearer aceita os dois).
                using var porCookie = await _f.Api.EnviarAsync(rota.Http, rota.Url(), token: token);
                if (porCookie.StatusCode != HttpStatusCode.Unauthorized) falhas.Add($"cookie/{nome}: {(int)porCookie.StatusCode}");

                using var req = ClienteApi.Requisicao(rota.Http, rota.Url(), ApiRealFixture.NovoIp());
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                using var porHeader = await _f.Api.Http.SendAsync(req);
                if (porHeader.StatusCode != HttpStatusCode.Unauthorized) falhas.Add($"header/{nome}: {(int)porHeader.StatusCode}");
            }

            Assert.True(falhas.Count == 0, $"{chave} aceitou credencial inválida: {string.Join("; ", falhas)}");
        }

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.Protegidas), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaProtegida_TokenValidoDoPapelCerto_NaoResponde401(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            var d = _f.Dados;

            Sessao sessao;
            if (rota.EncerraSessao)
                sessao = await _f.LoginAsync((await _f.NovoParAsync()).Contratante);   // descartável
            else
                sessao = await _f.SessaoAsync(rota.Guarda switch
                {
                    Guarda.Admin => d.Admin,
                    Guarda.Prestador => d.PrestadorVarredura,
                    _ => d.ContratanteVarredura
                });

            using var r = await _f.Api.EnviarAsync(rota.Http, rota.Url(), sessao);
            Assert.True(r.StatusCode != HttpStatusCode.Unauthorized,
                $"{chave} recusou um token válido do papel certo: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.Publicas), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaPublica_SemToken_NaoExigeAutenticacao(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            using var r = await _f.Api.EnviarAsync(rota.Http, rota.Url());

            if (MatrizEndpoints.PublicasQueRecusamSemCredencialPropria.Contains(chave))
                Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);   // refresh sem cookie / webhook sem token
            else
                Assert.True(r.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
                    $"{chave} deveria ser pública, respondeu {(int)r.StatusCode}");
        }

        [FactApiReal]
        public async Task Webhook_ComTokenErrado_Recusa_ComTokenCerto_Aceita()
        {
            using var errado = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/asaas")
            { Content = new StringContent("{\"event\":\"PAYMENT_RECEIVED\"}", System.Text.Encoding.UTF8, "application/json") };
            errado.Headers.Add("asaas-access-token", "token-errado");
            using var r1 = await _f.Api.Http.SendAsync(errado);

            using var certo = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/asaas")
            { Content = new StringContent("{\"event\":\"EVENTO_DESCONHECIDO_DE_TESTE\"}", System.Text.Encoding.UTF8, "application/json") };
            certo.Headers.Add("asaas-access-token", ApiRealFixture.WebhookToken);
            using var r2 = await _f.Api.Http.SendAsync(certo);

            Assert.Equal(HttpStatusCode.Unauthorized, r1.StatusCode);
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Ciclo de vida da credencial
        // ─────────────────────────────────────────────────────────────────────

        [FactApiReal]
        public async Task Logout_InvalidaOTokenNaHora()
        {
            var s = await _f.LoginAsync((await _f.NovoParAsync()).Contratante);
            using (var antes = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/me", s))
                Assert.Equal(HttpStatusCode.OK, antes.StatusCode);

            using (var logout = await _f.Api.EnviarAsync(HttpMethod.Post, "/api/auth/logout", s))
                Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

            using var depois = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/me", s);
            Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode);
            Assert.Equal("SESSAO_ENCERRADA", await ClienteApi.CodigoErroAsync(depois));
        }

        [FactApiReal]
        public async Task SessaoDeUmaReplica_ValeNaOutra_ERevogacaoTambem()
        {
            var s = await _f.LoginAsync((await _f.NovoParAsync()).Contratante, _f.Api);
            using (var naReplica = await _f.Replica.EnviarAsync(HttpMethod.Get, "/api/me", s))
                Assert.Equal(HttpStatusCode.OK, naReplica.StatusCode);

            using (await _f.Api.EnviarAsync(HttpMethod.Post, "/api/auth/logout", s)) { }

            using var depois = await _f.Replica.EnviarAsync(HttpMethod.Get, "/api/me", s);
            Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode);
        }

        [FactApiReal]
        public async Task ContaExcluida_TokenAntigoResponde401_ELoginFalha()
        {
            var u = (await _f.NovoParAsync()).Contratante;
            var s = await _f.LoginAsync(u);
            await _f.ReautenticarAsync(s);

            using (var excluir = await _f.Api.EnviarAsync(HttpMethod.Delete, "/usuarios/conta", s))
                Assert.Equal(HttpStatusCode.OK, excluir.StatusCode);

            using var depois = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/me", s);
            using var login = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", corpo: new { email = u.Email, senha = ApiRealFixture.Senha });

            Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, login.StatusCode);
        }

        [FactApiReal]
        public async Task UsuarioBloqueadoOuPendente_NaoConsegueLogar()
        {
            var bloqueado = (await _f.NovoParAsync()).Contratante;
            var pendente = (await _f.NovoParAsync()).Contratante;
            await using (var db = _f.Db())
            {
                await db.Usuarios.Where(x => x.Id == bloqueado.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, StatusUsuario.Blocked));
                await db.Usuarios.Where(x => x.Id == pendente.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, StatusUsuario.Pending));
            }

            foreach (var u in new[] { bloqueado, pendente })
            {
                using var r = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", corpo: new { email = u.Email, senha = ApiRealFixture.Senha });
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.False(r.Headers.Contains("Set-Cookie"), "Login recusado não pode emitir cookie de sessão.");
            }
        }

        [FactAchado("D11")]
        public async Task UsuarioBloqueadoComSessaoAberta_PerdeOAcesso()
        {
            var u = (await _f.NovoParAsync()).Contratante;
            var s = await _f.LoginAsync(u);

            await using (var db = _f.Db())
                await db.Usuarios.Where(x => x.Id == u.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, StatusUsuario.Blocked));

            using var r = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/me/contratos", s);
            Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"Usuário bloqueado continuou acessando com a sessão aberta: {(int)r.StatusCode}");
        }
    }
}
