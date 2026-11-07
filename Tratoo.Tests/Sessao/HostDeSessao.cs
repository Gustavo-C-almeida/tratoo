using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Tratoo.API.EndPoints;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Infrastructure;
using Tratoo.Domain.Models;
using Tratoo.Tests.Integracao;

namespace Tratoo.Tests.Sessao
{
    /// <summary>
    /// API em memória com o MESMO caminho de autenticação da produção:
    /// <c>AddTratooAutenticacao</c> (JwtBearer + conferência da sessão), <c>UseVerificacaoDeSessao</c>,
    /// os endpoints reais de <c>usuarios/*</c>, <c>/api/auth/*</c> e <c>/api/conta/*</c>, e o
    /// <c>LoginService</c> real (tempo mínimo de 300 ms incluso). Só o banco é falso.
    ///
    /// Rotas extras de teste: <c>/teste/comum</c>, <c>/teste/sensivel</c> (RequireSessaoVerificada)
    /// e <c>/teste/reauth</c> (ExigirReautenticacaoRecente).
    /// </summary>
    internal sealed class HostDeSessao : IAsyncDisposable
    {
        public const string Senha = "Senha@Forte1";

        public WebApplication App { get; }
        public UsuariosFalsos Usuarios { get; }
        public AuditoriaFalsa Auditoria { get; }
        public EmailsFalsos Emails { get; }
        public StoreComFalha Store { get; }

        private HostDeSessao(WebApplication app, UsuariosFalsos usuarios, AuditoriaFalsa auditoria, EmailsFalsos emails, StoreComFalha store)
        {
            App = app;
            Usuarios = usuarios;
            Auditoria = auditoria;
            Emails = emails;
            Store = store;
        }

        public static Dictionary<string, string?> ConfigBase() => new()
        {
            ["Jwt:SecretKey"] = "chave-jwt-exclusiva-dos-testes-de-sessao-0123456789",
            ["Jwt:Issuer"] = "Tratoo",
            ["Jwt:Audience"] = "TratooUsers",
        };

        /// <summary>
        /// Sobe a API. Sem <c>Redis:ConnectionString</c> em <paramref name="config"/>, sessões e
        /// OTP ficam na memória; com ela, no Redis (duas chamadas = duas réplicas).
        /// </summary>
        public static async Task<HostDeSessao> SubirAsync(
            IDictionary<string, string?>? config = null,
            TimeProvider? relogio = null,
            UsuariosFalsos? usuarios = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            var todas = ConfigBase();
            foreach (var (k, v) in config ?? new Dictionary<string, string?>())
                todas[k] = v;
            builder.Configuration.AddInMemoryCollection(todas);

            usuarios ??= new UsuariosFalsos();
            var auditoria = new AuditoriaFalsa();
            var emails = new EmailsFalsos();

            var s = builder.Services;
            if (relogio is not null)
                s.AddSingleton(relogio);
            s.AddTratooEstadoEfemero(builder.Configuration);
            s.AddTratooRateLimiter();
            s.AddTratooAutenticacao(builder.Configuration);

            // Embrulha o store escolhido pela configuração para simular a queda do armazenamento.
            var descritor = s.Single(d => d.ServiceType == typeof(ISessaoStore));
            s.Remove(descritor);
            s.AddSingleton(sp => new StoreComFalha((ISessaoStore)ActivatorUtilities.CreateInstance(sp, descritor.ImplementationType!)));
            s.AddSingleton<ISessaoStore>(sp => sp.GetRequiredService<StoreComFalha>());

            s.AddSingleton<IUsuarioRepository>(usuarios);
            s.AddSingleton<IAuditLogRepository>(auditoria);
            s.AddSingleton(emails.Servico());
            s.AddScoped<IJwtService, JwtService>();
            s.AddScoped<IVerificacaoMFAService, VerificacaoMFAService>();
            s.AddScoped<ILoginService, LoginService>();
            s.AddScoped<IContaService, ContaService>();
            s.AddSingleton(Falso<ICadastroService>.Criar());
            s.AddSingleton(Falso<IIdentidadeService>.Criar());
            s.AddSingleton(Falso<IExclusaoContaService>.Criar());

            var app = builder.Build();

            // TestServer não tem conexão TCP: cada Navegador manda o "seu" IP por este header,
            // para não dividirem o mesmo balde de rate limit (no Redis, o balde é global).
            app.Use(async (http, next) =>
            {
                if (IPAddress.TryParse(http.Request.Headers[Navegador.HeaderIp].ToString(), out var ip))
                    http.Connection.RemoteIpAddress = ip;
                await next();
            });

            app.UseTratooTratamentoDeErros();
            app.UseAuthentication();
            app.UseVerificacaoDeSessao();
            app.UseAuthorization();
            app.UseRateLimiter();

            app.AddEndPointsUsers();
            app.AddEndPointsAuth();

            app.MapGet("/teste/comum", (HttpContext http) =>
                Results.Ok(new { sid = SessaoAtual.Sid(http), estado = SessaoAtual.Estado(http).ToString() })).RequireAuthorization();
            app.MapGet("/teste/sensivel", () => Results.Ok()).RequireAuthorization().RequireSessaoVerificada();
            app.MapPost("/teste/reauth", () => Results.Ok()).RequireAuthorization().ExigirReautenticacaoRecente();

            await app.StartAsync();
            var host = new HostDeSessao(app, usuarios, auditoria, emails, (StoreComFalha)app.Services.GetRequiredService<ISessaoStore>());
            return host;
        }

        public Navegador NovoNavegador(string userAgent = "Mozilla/5.0 (Testes) Navegador") => new(App.GetTestClient(), userAgent);

        /// <summary>Login completo (com o passo de MFA quando a conta exige).</summary>
        public async Task<HttpResponseMessage> EntrarAsync(Navegador nav, Usuario usuario, bool manterConectado = false)
        {
            var r = await nav.PostAsync("/usuarios/login", new { email = usuario.Email, senha = Senha, manterConectado });
            r.EnsureSuccessStatusCode();
            var corpo = await r.Content.ReadFromJsonAsync<RespostaLogin>();
            if (corpo?.RequerMFA != true)
                return r;

            var codigo = Emails.UltimoCodigo(usuario.Email)!;
            r = await nav.PostAsync("/usuarios/login/mfa", new { email = usuario.Email, codigo, manterConectado });
            r.EnsureSuccessStatusCode();
            return r;
        }

        public async ValueTask DisposeAsync() => await App.DisposeAsync();

        private sealed record RespostaLogin(bool RequerMFA);
    }

    /// <summary>
    /// Cliente com cookies, respeitando o Path (o <c>tratoo_refresh</c> só vai para /api/auth).
    /// Guarda os Set-Cookie crus para os testes conferirem os atributos.
    /// </summary>
    internal sealed class Navegador
    {
        public const string HeaderIp = "X-Test-Ip";

        private readonly HttpClient _cliente;
        private readonly string _userAgent;
        private readonly Dictionary<string, (string Valor, string Caminho)> _cookies = new();

        public List<string> SetCookies { get; } = new();

        /// <summary>IP de documentação (RFC 3849), único por navegador.</summary>
        public string Ip { get; } = $"2001:db8::{Random.Shared.Next(1, 0xffff):x}:{Random.Shared.Next(1, 0xffff):x}";

        public Navegador(HttpClient cliente, string userAgent)
        {
            _cliente = cliente;
            _userAgent = userAgent;
        }

        public string? Cookie(string nome) => _cookies.TryGetValue(nome, out var c) ? c.Valor : null;

        public void DefinirCookie(string nome, string valor, string caminho = "/") => _cookies[nome] = (valor, caminho);

        /// <summary>Sid da sessão atual (primeira parte do cookie de refresh).</summary>
        public string? Sid => Cookie(SessaoAtual.NomeCookieRefresh)?.Split('.')[0];

        public Task<HttpResponseMessage> GetAsync(string url, bool passivo = false) => EnviarAsync(HttpMethod.Get, url, null, passivo);
        public Task<HttpResponseMessage> PostAsync(string url, object? corpo = null) => EnviarAsync(HttpMethod.Post, url, corpo, false);
        public Task<HttpResponseMessage> PutAsync(string url, object? corpo = null) => EnviarAsync(HttpMethod.Put, url, corpo, false);
        public Task<HttpResponseMessage> DeleteAsync(string url) => EnviarAsync(HttpMethod.Delete, url, null, false);

        public async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, object? corpo, bool passivo)
        {
            var req = new HttpRequestMessage(metodo, url);
            req.Headers.UserAgent.ParseAdd(_userAgent);
            req.Headers.Add(HeaderIp, Ip);
            if (passivo)
                req.Headers.Add(SessaoAtual.HeaderPassivo, "1");
            if (corpo is not null)
                req.Content = JsonContent.Create(corpo);

            var caminho = new Uri(new Uri("http://localhost"), url).AbsolutePath;
            var aplicaveis = _cookies.Where(c => caminho.StartsWith(c.Value.Caminho, StringComparison.Ordinal))
                .Select(c => $"{c.Key}={c.Value.Valor}").ToList();
            if (aplicaveis.Count > 0)
                req.Headers.Add("Cookie", string.Join("; ", aplicaveis));

            var resposta = await _cliente.SendAsync(req);

            if (resposta.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var sc in setCookies)
                {
                    SetCookies.Add(sc);
                    var partes = sc.Split(';').Select(p => p.Trim()).ToArray();
                    var nomeValor = partes[0].Split('=', 2);
                    var caminhoCookie = partes.FirstOrDefault(p => p.StartsWith("path=", StringComparison.OrdinalIgnoreCase))?[5..] ?? "/";
                    var expirado = partes.Any(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase)
                        && DateTimeOffset.TryParse(p[8..], out var e) && e < DateTimeOffset.UtcNow);
                    if (expirado || nomeValor[1].Length == 0)
                        _cookies.Remove(nomeValor[0]);
                    else
                        _cookies[nomeValor[0]] = (nomeValor[1], caminhoCookie);
                }
            }
            return resposta;
        }
    }

    /// <summary>Relógio controlado pelo teste: a expiração das sessões em memória segue ele.</summary>
    internal sealed class RelogioDeTeste : TimeProvider
    {
        private DateTimeOffset _agora = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _agora;
        public void Avancar(TimeSpan quanto) => _agora += quanto;
    }

    /// <summary>Decorador que simula o Redis fora do ar (lança como o SessaoStoreRedis lançaria).</summary>
    internal sealed class StoreComFalha : ISessaoStore
    {
        private readonly ISessaoStore _interno;
        public volatile bool Indisponivel;

        public StoreComFalha(ISessaoStore interno) => _interno = interno;
        public ISessaoStore Interno => _interno;

        private T Ou<T>(Func<T> f) => Indisponivel ? throw new ServicoIndisponivelException("Redis fora (simulado).") : f();

        public Task CriarAsync(string sid, DadosSessao sessao, TimeSpan ttlSessao, string refreshToken, TimeSpan ttlRefresh) =>
            Ou(() => _interno.CriarAsync(sid, sessao, ttlSessao, refreshToken, ttlRefresh));
        public Task<DadosSessao?> ObterAsync(string sid) => Ou(() => _interno.ObterAsync(sid));
        public Task<bool> AtualizarSeExisteAsync(string sid, DadosSessao sessao, TimeSpan ttl) => Ou(() => _interno.AtualizarSeExisteAsync(sid, sessao, ttl));
        public Task<bool> RemoverAsync(string sid, int usuarioId) => Ou(() => _interno.RemoverAsync(sid, usuarioId));
        public Task<IReadOnlyList<(string Sid, DadosSessao Sessao)>> ListarDoUsuarioAsync(int usuarioId) => Ou(() => _interno.ListarDoUsuarioAsync(usuarioId));
        public Task<string?> ObterSidDoRefreshAsync(string refreshToken) => Ou(() => _interno.ObterSidDoRefreshAsync(refreshToken));
        public Task<bool> ConsumirRefreshAsync(string refreshToken, string sid, RefreshUsado marca, TimeSpan ttlMarca) =>
            Ou(() => _interno.ConsumirRefreshAsync(refreshToken, sid, marca, ttlMarca));
        public Task GravarRefreshAsync(string refreshToken, string sid, TimeSpan ttl) => Ou(() => _interno.GravarRefreshAsync(refreshToken, sid, ttl));
        public Task<RefreshUsado?> ObterRefreshUsadoAsync(string refreshToken) => Ou(() => _interno.ObterRefreshUsadoAsync(refreshToken));
        public Task MarcarReautenticacaoAsync(string sid, TimeSpan ttl) => Ou(() => _interno.MarcarReautenticacaoAsync(sid, ttl));
        public Task<bool> ReautenticouRecentementeAsync(string sid) => Ou(() => _interno.ReautenticouRecentementeAsync(sid));
    }

    internal sealed class UsuariosFalsos : IUsuarioRepository
    {
        private readonly ConcurrentDictionary<int, Usuario> _porId = new();
        // Contador ÚNICO no processo, não por instância: os testes com Redis real compartilham
        // o mesmo servidor (RedisFixture), e IDs repetidos entre hosts faziam um teste herdar as
        // sessões que outro deixou em "sessoes-usuario:{id}".
        private static int _proximo = 100;

        /// <summary>BCrypt com custo 4: o hash é verificado de verdade, mas sem pagar o custo de produção.</summary>
        public Contratante Novo(bool mfa = false, bool admin = false)
        {
            var id = Interlocked.Increment(ref _proximo);
            var u = new Contratante
            {
                Id = id,
                Nome = $"Pessoa {id}",
                Email = $"pessoa{id}.{Guid.NewGuid():N}@exemplo.com",
                SenhaHash = BCrypt.Net.BCrypt.HashPassword(HostDeSessao.Senha, 4),
                TipoUsuario = TipoUsuario.Contratante,
                Status = StatusUsuario.Active,
                MFA = mfa,
                IsAdmin = admin
            };
            _porId[id] = u;
            return u;
        }

        public Task<bool> EmailExisteAsync(string email) => Task.FromResult(_porId.Values.Any(u => u.Email == email));
        public Task SalvarAsync(Usuario user) { _porId[user.Id] = user; return Task.CompletedTask; }
        public Task AtualizarAsync(Usuario user) { _porId[user.Id] = user; return Task.CompletedTask; }
        public Task<Usuario?> ObterPorEmailAsync(string email) =>
            Task.FromResult(_porId.Values.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));
        public Task<Usuario?> ObterPorIdAsync(int id) => Task.FromResult(_porId.TryGetValue(id, out var u) ? u : null);
    }

    internal sealed class AuditoriaFalsa : IAuditLogRepository
    {
        public ConcurrentQueue<(int UserId, string Acao, string Ip)> Registros { get; } = new();

        public Task RegistrarAsync(int userId, string acao, string ip)
        {
            Registros.Enqueue((userId, acao, ip));
            return Task.CompletedTask;
        }

        public int Contar(int userId, string acao) => Registros.Count(r => r.UserId == userId && r.Acao == acao);
    }

    /// <summary>Guarda o último código enviado por e-mail; o resto do IEmailService não faz nada.</summary>
    internal sealed class EmailsFalsos
    {
        private readonly ConcurrentDictionary<string, string> _codigos = new(StringComparer.OrdinalIgnoreCase);

        public string? UltimoCodigo(string email) => _codigos.TryGetValue(email, out var c) ? c : null;

        public IEmailService Servico()
        {
            object? Guardar(object?[] a) { _codigos[(string)a[0]!] = (string)a[1]!; return Task.CompletedTask; }
            return Falso<IEmailService>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                [nameof(IEmailService.EnviarCodigoVerificacaoAsync)] = Guardar,
                [nameof(IEmailService.EnviarCodigoResetSenhaAsync)] = Guardar
            });
        }
    }

    internal static class Respostas
    {
        public static async Task<string?> CodigoErroAsync(HttpResponseMessage r)
        {
            try
            {
                var corpo = await r.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
                return corpo is not null && corpo.TryGetValue("codigoErro", out var c) ? c?.ToString() : null;
            }
            catch { return null; }
        }
    }
}
