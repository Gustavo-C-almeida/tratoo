using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Models.Financeiro;
using Xunit;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// Fase 3 da auditoria — papéis (contratante × prestador × admin) contra a API real.
    /// </summary>
    [Collection(ColecaoApiReal.Nome)]
    public sealed class AutorizacaoPapelTests
    {
        private readonly ApiRealFixture _f;
        public AutorizacaoPapelTests(ApiRealFixture f) => _f = f;

        // ─────────────────────────────────────────────────────────────────────
        // Contratante × Prestador
        // ─────────────────────────────────────────────────────────────────────

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.SoContratante), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaDeContratante_ComTokenDePrestador_Responde403(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            var prestador = await _f.SessaoAsync(_f.Dados.PrestadorVarredura);
            using var r = await _f.Api.EnviarAsync(rota.Http, rota.Url(), prestador);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.SoPrestador), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaDePrestador_ComTokenDeContratante_Responde403(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            var contratante = await _f.SessaoAsync(_f.Dados.ContratanteVarredura);
            using var r = await _f.Api.EnviarAsync(rota.Http, rota.Url(), contratante);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        /// <summary>
        /// Rotas cuja regra de negócio é de um papel, mas que só exigem "autenticado" — o papel é
        /// checado no serviço e a recusa vem como 400. A correção esperada é a policy na rota (403).
        /// </summary>
        [TheoryAchado("D6")]
        [MemberData(nameof(MatrizEndpoints.PapelSoNoServico), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaDeUmPapel_ComTokenDoOutroPapel_Responde403(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            var outro = await _f.SessaoAsync(rota.Intencao == Intencao.Prestador ? _f.Dados.ContratanteVarredura : _f.Dados.PrestadorVarredura);
            using var r = await _f.Api.EnviarAsync(rota.Http, rota.Url(), outro);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Admin
        // ─────────────────────────────────────────────────────────────────────

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.SoAdmin), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaDeAdmin_ComTokenDeContratanteOuPrestador_Responde403(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            foreach (var u in new[] { _f.Dados.ContratanteVarredura, _f.Dados.PrestadorVarredura })
            {
                using var r = await _f.Api.EnviarAsync(rota.Http, rota.Url(), await _f.SessaoAsync(u));
                Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{chave} com {u.TipoUsuario}: {(int)r.StatusCode}");
            }
        }

        [TheoryApiReal]
        [MemberData(nameof(MatrizEndpoints.SoAdmin), MemberType = typeof(MatrizEndpoints))]
        public async Task RotaDeAdmin_ComTruquesDoCliente_ContinuaRecusando(string chave)
        {
            var rota = MatrizEndpoints.Por(chave);
            var contratante = await _f.SessaoAsync(_f.Dados.ContratanteVarredura);
            var url = rota.Url() + (rota.Url().Contains('?') ? "&" : "?") + "admin=true&isAdmin=true&role=Admin";

            using var req = ClienteApi.Requisicao(rota.Http, url, contratante.Ip, contratante.Token,
                corpo: rota.Metodo is "POST" or "PUT" ? new { isAdmin = true, role = "Admin", favorContratante = true, notaResolucao = "tentativa de escalada" } : null);
            foreach (var (h, v) in new[] { ("X-Admin", "true"), ("X-Role", "Admin"), ("X-User-Role", "Admin"), ("X-Is-Admin", "1") })
                req.Headers.TryAddWithoutValidation(h, v);
            using var r = await _f.Api.Http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

            // Token com papel Admin acrescentado, mas assinado por quem não tem a chave.
            var forjado = ApiRealFixture.ForjarToken(contratante.Usuario, contratante.Sid, "chave-do-atacante-com-mais-de-32-bytes-0000",
                DateTime.UtcNow.AddMinutes(5), papeisExtras: new[] { "Admin" });
            using var r2 = await _f.Api.EnviarAsync(rota.Http, rota.Url(), token: forjado);
            Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
        }

        [FactApiReal]
        public async Task Admin_AcessaAsRotasDeLeitura()
        {
            var admin = await _f.SessaoAsync(_f.Dados.Admin);
            foreach (var url in new[] { "/api/admin/disputas", $"/api/admin/disputas/{_f.Dados.DisputaD}", "/api/diagnostico/rede" })
            {
                using var r = await _f.Api.EnviarAsync(HttpMethod.Get, url, admin);
                Assert.True(r.StatusCode == HttpStatusCode.OK, $"{url}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
            }
        }

        [FactApiReal]
        public async Task ResolverDisputa_SoAdminComReautenticacao_UmaVez_ComAuditoria()
        {
            var d = _f.Dados;
            var url = $"/api/admin/disputas/{d.DisputaD}/resolver";
            var corpo = new { favorContratante = true, notaResolucao = "Decisão dos testes de segurança: estorno integral." };

            // As partes da própria disputa — mesmo reautenticadas — não resolvem.
            foreach (var parte in new[] { d.ContratanteD, d.PrestadorD })
            {
                var s = await _f.LoginAsync(parte);
                await _f.ReautenticarAsync(s);
                using var r = await _f.Api.EnviarAsync(HttpMethod.Post, url, s, corpo);
                Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"Parte ({parte.TipoUsuario}) resolveu a própria disputa: {(int)r.StatusCode}");
            }

            // Admin sem reautenticação recente: 403 REAUTH_REQUIRED.
            var admin = await _f.LoginAsync(d.Admin);
            using (var semReauth = await _f.Api.EnviarAsync(HttpMethod.Post, url, admin, corpo))
            {
                Assert.Equal(HttpStatusCode.Forbidden, semReauth.StatusCode);
                Assert.Equal("REAUTH_REQUIRED", await ClienteApi.CodigoErroAsync(semReauth));
            }

            // Admin reautenticado (senha + MFA): resolve.
            await _f.ReautenticarAsync(admin);
            using (var ok = await _f.Api.EnviarAsync(HttpMethod.Post, url, admin, corpo))
                Assert.True(ok.StatusCode == HttpStatusCode.OK, $"Admin não resolveu: {(int)ok.StatusCode} {await ok.Content.ReadAsStringAsync()}");

            // Segunda resolução: recusada pela regra de negócio.
            using (var deNovo = await _f.Api.EnviarAsync(HttpMethod.Post, url, admin, corpo))
                Assert.Equal(HttpStatusCode.BadRequest, deNovo.StatusCode);

            await using var db = _f.Db();
            var disputa = await db.Set<DisputaPagamento>().AsNoTracking().SingleAsync(x => x.Id == d.DisputaD);
            Assert.Equal(StatusDisputa.ResolvidaAFavorContratante, disputa.Status);
            Assert.Equal(d.Admin.Id, disputa.ResolvidoPorId);
            Assert.NotNull(disputa.ResolvidaEm);

            var auditoria = await db.AuditLogs.AsNoTracking()
                .Where(a => a.UserId == d.Admin.Id && a.Acao == "disputa_resolvida").ToListAsync();
            Assert.Single(auditoria);
            Assert.Equal(admin.Ip, auditoria[0].Ip);   // IP real do cliente, não o do proxy
        }

        [FactApiReal]
        public async Task Cadastro_NaoCriaAdmin_NemComCamposExtras()
        {
            var ip = ApiRealFixture.NovoIp();

            // Tipo "admin" não existe no cadastro. (Hoje responde 500 em vez de 400 — achado D8.)
            foreach (var tipo in new[] { "admin", "Admin", "ADMIN" })
            {
                var pedido = Cadastro("tipo.admin", tipo);
                using var tipoAdmin = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/cadastro", ip: ApiRealFixture.NovoIp(), corpo: pedido.Corpo);
                Assert.False(tipoAdmin.IsSuccessStatusCode, $"Cadastro com tipo '{tipo}' foi aceito: {(int)tipoAdmin.StatusCode}");
                await using var dbTipo = _f.Db();
                Assert.False(await dbTipo.Usuarios.AnyAsync(x => x.Email == pedido.Email));
            }

            // Contratante com campos de privilégio no corpo: ignorados.
            var email = $"escalada.{Guid.NewGuid():N}"[..24] + "@teste.local";
            var desde = DateTime.UtcNow.AddSeconds(-1);
            var corpo = new Dictionary<string, object?>
            {
                ["nome"] = "Tentativa de Escalada", ["email"] = email, ["senha"] = ApiRealFixture.Senha,
                ["confirmarSenha"] = ApiRealFixture.Senha, ["mfa"] = false, ["tipo"] = "contratante", ["aceitouTermos"] = true,
                ["isAdmin"] = true, ["IsAdmin"] = true, ["role"] = "Admin", ["tipoUsuario"] = "Admin", ["status"] = "Active"
            };
            using (var cad = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/cadastro", ip: ip, corpo: corpo))
                Assert.True(cad.StatusCode == HttpStatusCode.OK, $"Cadastro: {(int)cad.StatusCode} {await cad.Content.ReadAsStringAsync()}");
            var codigo = await _f.Externo.CodigoEnviadoParaAsync(email, desde);
            using (var conf = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/cadastro/confirmar", ip: ip,
                       corpo: new { email, codigo, isAdmin = true }))
                Assert.Equal(HttpStatusCode.OK, conf.StatusCode);

            await using var db = _f.Db();
            var u = await db.Usuarios.AsNoTracking().SingleAsync(x => x.Email == email);
            Assert.False(u.IsAdmin);
            Assert.Equal(Tratoo.Domain.Models.TipoUsuario.Contratante, u.TipoUsuario);

            using var login = await _f.Api.EnviarAsync(HttpMethod.Post, "/usuarios/login", corpo: new { email, senha = ApiRealFixture.Senha });
            var json = await ClienteApi.LerJsonAsync(login);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.False(json.GetProperty("isAdmin").GetBoolean());

            static (string Email, object Corpo) Cadastro(string nome, string tipo)
            {
                var email = $"{nome}.{Guid.NewGuid():N}"[..24] + "@teste.local";
                return (email, new { nome, email, senha = ApiRealFixture.Senha, confirmarSenha = ApiRealFixture.Senha, mfa = false, tipo, aceitouTermos = true });
            }
        }

        /// <summary>
        /// Admin rebaixado no banco: o papel vem do JWT (10 min) e da sessão; o acesso precisa
        /// cair. Resultado documentado no relatório.
        /// </summary>
        [FactAchado("D12")]
        public async Task AdminRebaixadoNoBanco_PerdeOAcessoAdministrativo()
        {
            var (contratante, _, _) = await _f.NovoParAsync();
            await using (var db = _f.Db())
                await db.Usuarios.Where(x => x.Id == contratante.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsAdmin, true));
            var s = await _f.LoginAsync(contratante);
            using (var antes = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/admin/disputas", s))
                Assert.Equal(HttpStatusCode.OK, antes.StatusCode);

            await using (var db = _f.Db())
                await db.Usuarios.Where(x => x.Id == contratante.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsAdmin, false));

            using var depois = await _f.Api.EnviarAsync(HttpMethod.Get, "/api/admin/disputas", s);
            Assert.True(depois.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
                $"Admin rebaixado no banco continuou acessando /api/admin: {(int)depois.StatusCode}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Health check
        // ─────────────────────────────────────────────────────────────────────

        [FactApiReal]
        public async Task HealthChecks_NaoVazamSegredosNemDetalhesDeExcecao()
        {
            foreach (var rota in new[] { "/health/live", "/health/ready" })
            {
                using var r = await _f.Api.EnviarAsync(HttpMethod.Get, rota);
                var corpo = await r.Content.ReadAsStringAsync();
                foreach (var proibido in new[] { "Password", "Username=", "Host=", "Database=", ApiRealFixture.ChaveJwt,
                             ConfiguracaoDosTestes.ChaveDeDadosDeTeste, ApiRealFixture.WebhookToken, "Exception", "   at ", "StackTrace" })
                    Assert.DoesNotContain(proibido, corpo, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Requisito da auditoria: health check só para admin autenticado (hoje anônimo).</summary>
        [FactAchado("D1")]
        public async Task HealthCheckDetalhado_SoParaAdmin()
        {
            using var anon = await _f.Api.EnviarAsync(HttpMethod.Get, "/health/ready");
            var corpo = await anon.Content.ReadAsStringAsync();
            Assert.True(anon.StatusCode == HttpStatusCode.Unauthorized || !corpo.Contains("checks"),
                $"/health/ready anônimo expõe o detalhe das dependências: {corpo}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Variações de método e caminho
        // ─────────────────────────────────────────────────────────────────────

        public static IEnumerable<object[]> VariacoesAdmin => new[]
        {
            new object[] { "GET", "/API/ADMIN/DISPUTAS" },
            new object[] { "GET", "/Api/Admin/Disputas" },
            new object[] { "GET", "/api/admin/disputas/" },
            new object[] { "GET", "//api/admin/disputas" },
            new object[] { "GET", "/api/admin/./disputas" },
            new object[] { "GET", "/api/x/../admin/disputas" },
            new object[] { "GET", "/api/admin/disputas%2f" },
            new object[] { "GET", "/api/admin/disputas?status=Aberta" },
            new object[] { "HEAD", "/api/admin/disputas" },
            new object[] { "OPTIONS", "/api/admin/disputas" },
            new object[] { "PUT", "/api/admin/disputas" },
            new object[] { "POST", "/api/admin/disputas" },
            new object[] { "DELETE", "/api/admin/disputas" },
            new object[] { "GET", "/api/admin/disputas/00000000-0000-0000-0000-0000000000aa/resolver" },
            new object[] { "GET", "/API/DIAGNOSTICO/REDE" },
        };

        [TheoryApiReal]
        [MemberData(nameof(VariacoesAdmin))]
        public async Task VariacaoDeMetodoOuCaminho_NaoContornaAGuardaDeAdmin(string metodo, string url)
        {
            var contratante = await _f.SessaoAsync(_f.Dados.ContratanteVarredura);
            using var anon = await _f.Api.EnviarAsync(new HttpMethod(metodo), url);
            using var comum = await _f.Api.EnviarAsync(new HttpMethod(metodo), url, contratante);

            Assert.False(anon.IsSuccessStatusCode, $"{metodo} {url} anônimo: {(int)anon.StatusCode}");
            Assert.False(comum.IsSuccessStatusCode, $"{metodo} {url} com contratante: {(int)comum.StatusCode}");
        }
    }
}
