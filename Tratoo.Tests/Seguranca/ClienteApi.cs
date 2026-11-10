using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Models;
using Xunit;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// Sessão autenticada obtida pelo fluxo REAL de login (inclusive MFA por e-mail). O token
    /// vai no cookie <c>tratoo_auth</c>, exatamente como o navegador envia.
    /// </summary>
    public sealed class Sessao
    {
        public required Usuario Usuario { get; init; }
        public required string Token { get; init; }
        public required string Ip { get; init; }
        public string Sid => new JwtSecurityTokenHandler().ReadJwtToken(Token).Claims.First(c => c.Type == JwtRegisteredClaimNames.Sid).Value;
    }

    public static class ClienteApi
    {
        /// <summary>
        /// Monta a requisição como ela chega ao Kestrel na Railway: o peer é o proxy (loopback,
        /// faixa confiável) e o X-Forwarded-For tem dois saltos — cliente e borda.
        /// </summary>
        public static HttpRequestMessage Requisicao(HttpMethod metodo, string url, string ip,
            string? token = null, object? corpo = null, HttpContent? conteudo = null)
        {
            var r = new HttpRequestMessage(metodo, url);
            r.Headers.TryAddWithoutValidation("X-Forwarded-For", $"{ip}, 100.64.0.7");
            if (token is not null)
                r.Headers.TryAddWithoutValidation("Cookie", $"tratoo_auth={token}");
            if (conteudo is not null)
                r.Content = conteudo;
            else if (corpo is not null)
                r.Content = JsonContent.Create(corpo);
            else if (metodo == HttpMethod.Post || metodo == HttpMethod.Put)
                r.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return r;
        }

        public static async Task<HttpResponseMessage> EnviarAsync(this ProcessoApiReal api, HttpMethod metodo, string url,
            Sessao? sessao = null, object? corpo = null, string? ip = null, string? token = null, HttpContent? conteudo = null)
        {
            // "//x" seria lido como URL relativa ao esquema (host "x"): manda o caminho cru, absoluto.
            if (url.StartsWith("//", StringComparison.Ordinal))
                url = api.BaseUrl.GetLeftPart(UriPartial.Authority) + url;
            using var r = Requisicao(metodo, url, ip ?? sessao?.Ip ?? ApiRealFixture.NovoIp(), token ?? sessao?.Token, corpo, conteudo);
            return await api.Http.SendAsync(r);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<Task<Sessao>>> _sessoes = new();

        /// <summary>Sessão reaproveitada por usuário (um login por usuário na suíte inteira).</summary>
        public static Task<Sessao> SessaoAsync(this ApiRealFixture f, Usuario u) =>
            _sessoes.GetOrAdd(u.Id, _ => new Lazy<Task<Sessao>>(() => f.LoginAsync(u))).Value;

        /// <summary>Par contratante/prestador novo (perfil completo, senha conhecida) para testes destrutivos.</summary>
        public static async Task<(Usuario Contratante, Usuario Prestador, Cenario Cenario)> NovoParAsync(this ApiRealFixture f)
        {
            var c = await Integracao.SeedIntegracao.CriarAsync(f.Conexao);
            await using var db = f.Db();
            var hash = BCrypt.Net.BCrypt.HashPassword(ApiRealFixture.Senha);
            await db.Usuarios.Where(x => x.Id == c.ContratanteId || x.Id == c.PrestadorId)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.SenhaHash, hash));
            var contratante = await db.Usuarios.AsNoTracking().SingleAsync(x => x.Id == c.ContratanteId);
            var prestador = await db.Usuarios.AsNoTracking().SingleAsync(x => x.Id == c.PrestadorId);
            return (contratante, prestador, Cenario.De(c));
        }

        public static async Task<Sessao> LoginAsync(this ApiRealFixture f, Usuario u, ProcessoApiReal? api = null, string? ip = null)
        {
            api ??= f.Api;
            ip ??= ApiRealFixture.NovoIp();
            var desde = DateTime.UtcNow.AddSeconds(-1);

            var resp = await api.EnviarAsync(HttpMethod.Post, "/usuarios/login", ip: ip,
                corpo: new { email = u.Email, senha = ApiRealFixture.Senha });
            var json = await LerJsonAsync(resp);
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"Login de {u.Email} falhou: {(int)resp.StatusCode} {json}");

            if (json.TryGetProperty("requerMFA", out var mfa) && mfa.GetBoolean())
            {
                var codigo = await f.Externo.CodigoEnviadoParaAsync(u.Email, desde);
                resp = await api.EnviarAsync(HttpMethod.Post, "/usuarios/login/mfa", ip: ip,
                    corpo: new { email = u.Email, codigo });
                Assert.True(resp.StatusCode == HttpStatusCode.OK, $"MFA de {u.Email} falhou: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
            }

            return new Sessao { Usuario = u, Ip = ip, Token = TokenDoCookie(resp) };
        }

        /// <summary>Reautenticação recente (senha e, se exigido, código MFA) para ações sensíveis.</summary>
        public static async Task ReautenticarAsync(this ApiRealFixture f, Sessao s, ProcessoApiReal? api = null)
        {
            api ??= f.Api;
            var desde = DateTime.UtcNow.AddSeconds(-1);
            var resp = await api.EnviarAsync(HttpMethod.Post, "/api/auth/reautenticar", s, new { senha = ApiRealFixture.Senha });
            var json = await LerJsonAsync(resp);
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"Reautenticação falhou: {(int)resp.StatusCode} {json}");
            if (json.TryGetProperty("requerMFA", out var mfa) && mfa.GetBoolean())
            {
                var codigo = await f.Externo.CodigoEnviadoParaAsync(s.Usuario.Email, desde);
                resp = await api.EnviarAsync(HttpMethod.Post, "/api/auth/reautenticar", s, new { senha = ApiRealFixture.Senha, codigo });
                Assert.True(resp.StatusCode == HttpStatusCode.OK, $"Reautenticação (MFA) falhou: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
            }
        }

        public static string TokenDoCookie(HttpResponseMessage resp)
        {
            Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies), "Resposta sem Set-Cookie.");
            var c = cookies!.First(x => x.StartsWith("tratoo_auth=", StringComparison.Ordinal));
            return c["tratoo_auth=".Length..c.IndexOf(';')];
        }

        public static async Task<JsonElement> LerJsonAsync(HttpResponseMessage r)
        {
            var texto = await r.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(texto)) return default;
            try { return JsonDocument.Parse(texto).RootElement.Clone(); }
            catch (JsonException) { return JsonDocument.Parse(JsonSerializer.Serialize(texto)).RootElement.Clone(); }
        }

        public static async Task<string?> CodigoErroAsync(HttpResponseMessage r)
        {
            var j = await LerJsonAsync(r);
            return j.ValueKind == JsonValueKind.Object && j.TryGetProperty("codigoErro", out var c) ? c.GetString() : null;
        }
    }
}
