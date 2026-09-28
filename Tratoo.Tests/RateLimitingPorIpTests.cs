using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.RateLimiting;
using Tratoo.API.Infrastructure;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// O rate limiting tem de contar por IP do cliente — o IP já corrigido pelos
    /// forwarded headers — e não por proxy nem globalmente.
    /// </summary>
    public class RateLimitingPorIpTests
    {
        private const string IpBorda    = "198.51.100.200"; // pool de borda (salto 2)
        private const string IpClienteA = "203.0.113.10";
        private const string IpClienteB = "203.0.113.20";

        /// <summary>Limite documentado de cada política, conforme RateLimiterSetup.</summary>
        public static readonly IEnumerable<object[]> TodasAsPoliticas = new[]
        {
            new object[] { "/cadastro",        5 },
            new object[] { "/login",          10 },
            new object[] { "/senha",           3 },
            new object[] { "/dados-bancarios", 5 },
            new object[] { "/otp-assinatura",  3 }
        };

        private static void MapearRotasLimitadas(WebApplication app)
        {
            app.MapGet("/cadastro", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaCadastro);

            app.MapGet("/login", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaLogin);

            app.MapGet("/senha", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaSenha);

            app.MapGet("/dados-bancarios", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaDadosBancarios);

            app.MapGet("/otp-assinatura", () => Results.Ok())
               .RequireRateLimiting(RateLimiterSetup.PoliticaOtpAssinatura);
        }

        /// <summary>Requisição com a cadeia de 2 saltos real, vinda de um peer confiável.</summary>
        private static Task<HttpResponseMessage> ChamarAsync(
            WebApplication app, string rota, string ipCliente)
        {
            var requisicao = new HttpRequestMessage(HttpMethod.Get, rota);
            requisicao.Headers.Add(TestHostFactory.HeaderPeer, TestHostFactory.PeerConfiavel);
            requisicao.Headers.Add("X-Forwarded-For", $"{ipCliente}, {IpBorda}");
            return app.GetTestClient().SendAsync(requisicao);
        }

        // ── Limites de cada política ─────────────────────────────────────────

        /// <summary>
        /// As cinco políticas — as quatro originais mais "cadastro" — respeitam
        /// exatamente o limite documentado: N passam, a N+1 é barrada.
        /// </summary>
        [Theory]
        [MemberData(nameof(TodasAsPoliticas))]
        public async Task CadaPolitica_RespeitaSeuLimite(string rota, int limite)
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= limite; i++)
                Assert.True((await ChamarAsync(app, rota, IpClienteA)).IsSuccessStatusCode,
                    $"{rota}: requisição {i} de {limite} deveria passar");

            var excedente = await ChamarAsync(app, rota, IpClienteA);
            Assert.Equal(HttpStatusCode.TooManyRequests, excedente.StatusCode);

            // Outro cliente não é afetado pelo limite do primeiro.
            Assert.True((await ChamarAsync(app, rota, IpClienteB)).IsSuccessStatusCode);
        }

        // ── Retry-After ──────────────────────────────────────────────────────

        /// <summary>
        /// Um 429 sem Retry-After faz o cliente re-tentar em loop imediato. O valor é
        /// igual à janela (60 s) — limite superior seguro para uma janela fixa.
        /// </summary>
        [Theory]
        [MemberData(nameof(TodasAsPoliticas))]
        public async Task Resposta429_TrazRetryAfterDeSessentaSegundos(string rota, int limite)
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= limite; i++)
                await ChamarAsync(app, rota, IpClienteA);

            var bloqueada = await ChamarAsync(app, rota, IpClienteA);

            Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
            Assert.True(
                bloqueada.Headers.Contains("Retry-After"),
                $"{rota}: resposta 429 deveria trazer Retry-After");
            Assert.Equal("60", bloqueada.Headers.GetValues("Retry-After").Single());
        }

        /// <summary>Resposta permitida não deve carregar Retry-After.</summary>
        [Fact]
        public async Task RespostaPermitida_NaoTrazRetryAfter()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            var ok = await ChamarAsync(app, "/login", IpClienteA);

            Assert.True(ok.IsSuccessStatusCode);
            Assert.False(ok.Headers.Contains("Retry-After"));
        }

        /// <summary>O corpo JSON em português foi preservado junto com o novo header.</summary>
        [Fact]
        public async Task Resposta429_PreservaMensagemJsonEmPortugues()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 3; i++)
                await ChamarAsync(app, "/senha", IpClienteA);

            var bloqueada = await ChamarAsync(app, "/senha", IpClienteA);
            var corpo = await bloqueada.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
            Assert.Contains("Muitas tentativas. Aguarde antes de tentar novamente.", corpo);
        }

        // ── Particionamento ──────────────────────────────────────────────────

        /// <summary>
        /// Política "login": 10/minuto. A 11ª do MESMO cliente é barrada; outro cliente,
        /// atrás do mesmo nó de borda, continua passando.
        /// </summary>
        [Fact]
        public async Task Login_ContaPorIpDoCliente_NaoPorBorda()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 10; i++)
                Assert.True((await ChamarAsync(app, "/login", IpClienteA)).IsSuccessStatusCode,
                    $"requisição {i} deveria passar");

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(app, "/login", IpClienteA)).StatusCode);

            Assert.True((await ChamarAsync(app, "/login", IpClienteB)).IsSuccessStatusCode);
        }

        /// <summary>
        /// Esgotar "senha" (3/min) não pode derrubar "login" do mesmo IP.
        /// Valida o comportamento tal como configurado em RateLimiterSetup (com o
        /// prefixo de política na chave). A prova de que o isolamento não DEPENDE
        /// desse prefixo está em
        /// <see cref="PoliticasComChaveCruaIdentica_AindaAssimNaoCompartilhamBalde"/>.
        /// </summary>
        [Fact]
        public async Task PoliticasDiferentes_NaoCompartilhamBalde()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 3; i++)
                Assert.True((await ChamarAsync(app, "/senha", IpClienteA)).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(app, "/senha", IpClienteA)).StatusCode);

            Assert.True((await ChamarAsync(app, "/login", IpClienteA)).IsSuccessStatusCode);
        }

        /// <summary>
        /// Corrige uma afirmação errada que constava do código e da documentação: NÃO é
        /// verdade que o middleware mantenha um único PartitionedRateLimiter em que
        /// chaves iguais colidem entre políticas.
        ///
        /// Aqui duas políticas devolvem a MESMA chave crua (só o IP, sem prefixo de
        /// política). Esgotar uma não afeta a outra — o isolamento é do próprio
        /// AddPolicy. Roda contra a API do framework, não contra o RateLimiterSetup,
        /// porque o que está sendo verificado é o comportamento do ASP.NET Core.
        /// </summary>
        [Fact]
        public async Task PoliticasComChaveCruaIdentica_AindaAssimNaoCompartilhamBalde()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            static void PorIpSemPrefixo(RateLimiterOptions options, string nome, int limite) =>
                options.AddPolicy(nome, http =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        // Chave crua, idêntica entre as duas políticas.
                        http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            Window = TimeSpan.FromMinutes(1),
                            PermitLimit = limite,
                            QueueLimit = 0
                        }));

            builder.Services.AddRateLimiter(options =>
            {
                PorIpSemPrefixo(options, "politicaA", 2);
                PorIpSemPrefixo(options, "politicaB", 10);
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });

            await using var app = builder.Build();

            app.Use(async (context, next) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.99");
                await next();
            });

            app.UseRateLimiter();
            app.MapGet("/a", () => Results.Ok()).RequireRateLimiting("politicaA");
            app.MapGet("/b", () => Results.Ok()).RequireRateLimiting("politicaB");

            await app.StartAsync();
            var client = app.GetTestClient();

            // Esgota politicaA (limite 2).
            Assert.True((await client.GetAsync("/a")).IsSuccessStatusCode);
            Assert.True((await client.GetAsync("/a")).IsSuccessStatusCode);
            Assert.Equal(
                HttpStatusCode.TooManyRequests, (await client.GetAsync("/a")).StatusCode);

            // politicaB, mesma chave crua, permanece intacta.
            Assert.True((await client.GetAsync("/b")).IsSuccessStatusCode);
        }

        /// <summary>
        /// Dois clientes reais distintos que caem no MESMO nó de borda precisam contar
        /// separado. Com ForwardLimit=1 ambos resolveriam para o IP da borda e
        /// dividiriam a cota.
        /// </summary>
        [Fact]
        public async Task DoisClientesNoMesmoNoDeBorda_NaoCompartilhamCota()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 3; i++)
                Assert.True((await ChamarAsync(app, "/senha", IpClienteA)).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(app, "/senha", IpClienteA)).StatusCode);

            Assert.True((await ChamarAsync(app, "/senha", IpClienteB)).IsSuccessStatusCode);
        }

        /// <summary>
        /// Sem os forwarded headers, todos os clientes chegam com o IP do peer e caem na
        /// mesma partição — o bug de disponibilidade que a correção elimina.
        /// </summary>
        [Fact]
        public async Task SemForwardedHeaders_ClientesDistintosCompartilhamOLimite()
        {
            await using var app = TestHostFactory.Criar(
                TestHostFactory.Config(habilitado: false),
                comRateLimiter: true,
                mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            for (var i = 1; i <= 3; i++)
                Assert.True((await ChamarAsync(app, "/senha", IpClienteA)).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarAsync(app, "/senha", IpClienteB)).StatusCode);
        }

        /// <summary>
        /// Um atacante falando direto com o Kestrel não consegue escapar do rate limit
        /// forjando X-Forwarded-For: o gate descarta o header e ele é contado pelo IP
        /// real da conexão.
        /// </summary>
        [Fact]
        public async Task PeerNaoConfiavel_NaoEscapaDoLimiteForjandoIp()
        {
            await using var app = TestHostFactory.Criar(
                comRateLimiter: true, mapearRotas: MapearRotasLimitadas);
            await app.StartAsync();

            // Cada requisição alega um IP de cliente diferente — se o header fosse
            // aceito, cada uma cairia numa partição nova e o limite nunca seria atingido.
            async Task<HttpResponseMessage> ChamarForjando(string ipFalso)
            {
                var requisicao = new HttpRequestMessage(HttpMethod.Get, "/senha");
                requisicao.Headers.Add(TestHostFactory.HeaderPeer, TestHostFactory.PeerNaoConfiavel);
                requisicao.Headers.Add("X-Forwarded-For", $"{ipFalso}, {IpBorda}");
                return await app.GetTestClient().SendAsync(requisicao);
            }

            Assert.True((await ChamarForjando("203.0.113.1")).IsSuccessStatusCode);
            Assert.True((await ChamarForjando("203.0.113.2")).IsSuccessStatusCode);
            Assert.True((await ChamarForjando("203.0.113.3")).IsSuccessStatusCode);

            // 4ª tentativa: o limite de 3/min já foi consumido pelo IP real do atacante.
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await ChamarForjando("203.0.113.4")).StatusCode);
        }

        /// <summary>
        /// Caracterização do bug original: RateLimiterOptions.AddFixedWindowLimiter não
        /// particiona por nada — usa uma chave constante. Roda contra a API do framework,
        /// não contra o código do Tratoo, para documentar por que a config antiga saiu.
        /// </summary>
        [Fact]
        public async Task AddFixedWindowLimiter_DoFramework_UsaUmUnicoBaldeGlobal()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            builder.Services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter("legado", cfg =>
                {
                    cfg.Window = TimeSpan.FromMinutes(1);
                    cfg.PermitLimit = 3;
                    cfg.QueueLimit = 0;
                    cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                });
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });

            await using var app = builder.Build();

            app.Use(async (context, next) =>
            {
                var peer = context.Request.Headers[TestHostFactory.HeaderPeer].ToString();
                if (IPAddress.TryParse(peer, out var endereco))
                    context.Connection.RemoteIpAddress = endereco;
                await next();
            });

            app.UseRateLimiter();
            app.MapGet("/legado", () => Results.Ok()).RequireRateLimiting("legado");

            await app.StartAsync();
            var client = app.GetTestClient();

            async Task<HttpResponseMessage> Chamar(string ip)
            {
                var requisicao = new HttpRequestMessage(HttpMethod.Get, "/legado");
                requisicao.Headers.Add(TestHostFactory.HeaderPeer, ip);
                return await client.SendAsync(requisicao);
            }

            Assert.True((await Chamar("203.0.113.1")).IsSuccessStatusCode);
            Assert.True((await Chamar("203.0.113.2")).IsSuccessStatusCode);
            Assert.True((await Chamar("203.0.113.3")).IsSuccessStatusCode);

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await Chamar("203.0.113.4")).StatusCode);
        }
    }
}
