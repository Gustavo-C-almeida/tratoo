using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Infrastructure;
using Tratoo.Domain.Models;
using Tratoo.Tests.Integracao;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Estado efêmero (OTP, tentativas, cadastro pendente) e o que acontece quando o
    /// armazenamento falha. Tudo aqui roda SEM Docker: memória, criptografia e um Redis
    /// propositalmente inalcançável. O comportamento com Redis real e duas réplicas está
    /// em Integracao/RedisReplicasTests.
    /// </summary>
    public class EstadoEfemeroTests
    {
        private static EstadoEfemeroEmMemoria NovaMemoria() => new(new MemoryCache(new MemoryCacheOptions()));

        private static ProtecaoEstadoEfemero NovaProtecao(byte seed = 7) =>
            new(Enumerable.Repeat(seed, 32).ToArray());

        /// <summary>Redis que nunca responde: porta 1 de 127.0.0.1, com as mesmas opções de produção.</summary>
        private static ConnectionMultiplexer RedisInalcancavel() =>
            ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { "127.0.0.1:1" },
                AbortOnConnectFail = false,
                ConnectTimeout = 500,
                BacklogPolicy = BacklogPolicy.FailFast
            });

        // ── Memória (produção com 1 réplica) ────────────────────────────────────

        [Fact]
        public async Task Memoria_guarda_le_e_remove_com_tipos_usados_pelos_servicos()
        {
            var estado = NovaMemoria();

            await estado.DefinirAsync("mfa:login:a@b.c", "hash", TimeSpan.FromMinutes(5));
            await estado.DefinirAsync("bancarios:edicao:1", true, TimeSpan.FromMinutes(5));

            Assert.Equal("hash", await estado.ObterAsync<string>("mfa:login:a@b.c"));
            Assert.True(await estado.ObterAsync<bool?>("bancarios:edicao:1"));
            Assert.Null(await estado.ObterAsync<string>("inexistente"));
            Assert.Null(await estado.ObterAsync<bool?>("inexistente"));

            await estado.RemoverAsync("mfa:login:a@b.c", "bancarios:edicao:1");
            Assert.Null(await estado.ObterAsync<string>("mfa:login:a@b.c"));
            Assert.Null(await estado.ObterAsync<bool?>("bancarios:edicao:1"));
        }

        [Fact]
        public async Task Memoria_expira_pelo_TTL()
        {
            var estado = NovaMemoria();
            await estado.DefinirAsync("curta", "x", TimeSpan.FromMilliseconds(100));
            await Task.Delay(300);
            Assert.Null(await estado.ObterAsync<string>("curta"));
        }

        [Fact]
        public async Task Memoria_incrementa_atomicamente_sob_concorrencia()
        {
            var estado = NovaMemoria();
            var resultados = await Task.WhenAll(Enumerable.Range(0, 200)
                .Select(_ => Task.Run(() => estado.IncrementarAsync("tentativas", TimeSpan.FromMinutes(1)))));

            // Cada incremento viu um valor diferente: nenhuma contagem perdida.
            Assert.Equal(Enumerable.Range(1, 200).Select(i => (long)i), resultados.OrderBy(x => x));
        }

        [Fact]
        public async Task Memoria_definir_se_ausente_so_vence_uma_vez()
        {
            var estado = NovaMemoria();
            var vencedores = await Task.WhenAll(Enumerable.Range(0, 50)
                .Select(_ => Task.Run(() => estado.DefinirSeAusenteAsync("marca", true, TimeSpan.FromMinutes(1)))));
            Assert.Equal(1, vencedores.Count(v => v));
        }

        // ── Proteção do conteúdo no Redis ───────────────────────────────────────

        [Fact]
        public void Nome_da_chave_esconde_o_email_e_mantem_so_a_categoria()
        {
            var protecao = NovaProtecao();
            var nome = protecao.NomeDaChave("mfa:login:fulano@exemplo.com");

            Assert.StartsWith("tratoo:mfa:", nome);
            Assert.DoesNotContain("fulano", nome);
            Assert.DoesNotContain("exemplo", nome);
            // Determinístico entre réplicas com o mesmo segredo...
            Assert.Equal(nome, NovaProtecao().NomeDaChave("mfa:login:fulano@exemplo.com"));
            // ...e diferente com outro segredo.
            Assert.NotEqual(nome, NovaProtecao(seed: 9).NomeDaChave("mfa:login:fulano@exemplo.com"));
        }

        [Fact]
        public void Valor_cifrado_nao_contem_o_texto_e_so_decifra_na_mesma_chave_com_o_mesmo_segredo()
        {
            var protecao = NovaProtecao();
            var claro = Encoding.UTF8.GetBytes("{\"email\":\"fulano@exemplo.com\"}");
            var nome = protecao.NomeDaChave("cadastro:fulano@exemplo.com");

            var cifrado = protecao.Cifrar(claro, nome);

            Assert.DoesNotContain("fulano", Encoding.Latin1.GetString(cifrado));
            Assert.Equal(claro, protecao.Decifrar(cifrado, nome));

            // Copiado para outra chave, adulterado ou com outro segredo: ilegível (null).
            Assert.Null(protecao.Decifrar(cifrado, protecao.NomeDaChave("cadastro:outro@exemplo.com")));
            var adulterado = (byte[])cifrado.Clone();
            adulterado[^1] ^= 0x01;
            Assert.Null(protecao.Decifrar(adulterado, nome));
            Assert.Null(NovaProtecao(seed: 9).Decifrar(cifrado, nome));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nao-e-base64!")]
        [InlineData("AAECAwQFBgc=")] // 8 bytes: curto demais
        public void Segredo_ausente_ou_fraco_derruba_a_subida(string? segredo)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [ProtecaoEstadoEfemero.ChaveConfiguracao] = segredo })
                .Build();

            Assert.Throws<InvalidOperationException>(() => ProtecaoEstadoEfemero.DeConfiguracao(config));
        }

        // ── MFA: uso único e limite de tentativas ───────────────────────────────

        [Fact]
        public async Task Codigo_MFA_vale_uma_unica_vez()
        {
            var mfa = new VerificacaoMFAService(NovaMemoria());
            var codigo = await mfa.GerarECriarAsync("a@b.c", "login");

            await mfa.ValidarAsync("a@b.c", codigo, "login");

            var ex = await Assert.ThrowsAsync<NegocioException>(() => mfa.ValidarAsync("a@b.c", codigo, "login"));
            Assert.Equal("Código expirado ou inválido", ex.Message);
        }

        [Fact]
        public async Task Codigo_MFA_e_invalidado_apos_o_limite_de_tentativas()
        {
            var mfa = new VerificacaoMFAService(NovaMemoria());
            var codigo = await mfa.GerarECriarAsync("a@b.c", "reset_senha");

            for (var i = 1; i < VerificacaoMFAService.MaxTentativas; i++)
            {
                var erro = await Assert.ThrowsAsync<NegocioException>(() => mfa.ValidarAsync("a@b.c", "000000", "reset_senha"));
                Assert.Equal("Código inválido", erro.Message);
            }

            var bloqueio = await Assert.ThrowsAsync<NegocioException>(() => mfa.ValidarAsync("a@b.c", "000000", "reset_senha"));
            Assert.Contains("Muitas tentativas", bloqueio.Message);

            // Nem o código certo vale mais.
            await Assert.ThrowsAsync<NegocioException>(() => mfa.ValidarAsync("a@b.c", codigo, "reset_senha"));
        }

        [Fact]
        public async Task Codigo_MFA_novo_zera_as_tentativas()
        {
            var mfa = new VerificacaoMFAService(NovaMemoria());
            await mfa.GerarECriarAsync("a@b.c", "login");
            for (var i = 1; i < VerificacaoMFAService.MaxTentativas; i++)
                await Assert.ThrowsAsync<NegocioException>(() => mfa.ValidarAsync("a@b.c", "000000", "login"));

            var novo = await mfa.GerarECriarAsync("a@b.c", "login");
            for (var i = 1; i < VerificacaoMFAService.MaxTentativas; i++)
                await Assert.ThrowsAsync<NegocioException>(() => mfa.ValidarAsync("a@b.c", "000000", "login"));

            await mfa.ValidarAsync("a@b.c", novo, "login"); // ainda dentro do limite do código novo
        }

        // ── Armazenamento fora do ar ────────────────────────────────────────────

        [Fact]
        public async Task Redis_fora_do_ar_OTP_falha_fechado_e_rapido()
        {
            using var redis = RedisInalcancavel();
            var estado = new EstadoEfemeroRedis(redis, NovaProtecao(), NullLogger<EstadoEfemeroRedis>.Instance);
            var mfa = new VerificacaoMFAService(estado);

            var relogio = Stopwatch.StartNew();
            await Assert.ThrowsAsync<ServicoIndisponivelException>(() => mfa.GerarECriarAsync("a@b.c", "login"));
            await Assert.ThrowsAsync<ServicoIndisponivelException>(() => mfa.ValidarAsync("a@b.c", "123456", "login"));
            await Assert.ThrowsAsync<ServicoIndisponivelException>(() => estado.IncrementarAsync("t", TimeSpan.FromMinutes(1)));

            // BacklogPolicy.FailFast: sem ficar esperando timeout por operação.
            Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(3), $"demorou {relogio.Elapsed}");
        }

        [Fact]
        public async Task Redis_fora_do_ar_rate_limit_degrada_para_o_limite_em_memoria()
        {
            using var redis = RedisInalcancavel();
            using var limitador = new LimitadorJanelaFixaRedis(redis, "tratoo:rl:teste",
                new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 },
                NullLogger.Instance);

            var obtidos = 0;
            for (var i = 0; i < 5; i++)
            {
                using var lease = await limitador.AcquireAsync(1);
                if (lease.IsAcquired) obtidos++;
            }

            // Nem bloqueia tudo (seria negar serviço), nem libera tudo: vale o limite local.
            Assert.Equal(3, obtidos);
        }

        /// <summary>
        /// Redis cujos comandos terminam CANCELADOS — o que o StackExchange.Redis faz, com
        /// BacklogPolicy.FailFast, aos comandos pendentes quando a conexão oscila. Visto na
        /// validação real com 2 réplicas: virava 500 numa rota com rate limit.
        /// </summary>
        private static IConnectionMultiplexer RedisQueCancelaComandos()
        {
            object Cancelada<T>() => Task.FromCanceled<T>(new CancellationToken(canceled: true));
            var banco = Falso<IDatabase>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                [nameof(IDatabase.ScriptEvaluateAsync)] = _ => Cancelada<RedisResult>(),
                [nameof(IDatabase.StringGetAsync)] = _ => Cancelada<RedisValue>(),
                [nameof(IDatabase.StringSetAsync)] = _ => Cancelada<bool>()
            });
            return Falso<IConnectionMultiplexer>.Criar(new Dictionary<string, Func<object?[], object?>>
            {
                [nameof(IConnectionMultiplexer.GetDatabase)] = _ => banco
            });
        }

        [Fact]
        public async Task Comando_cancelado_pelo_cliente_Redis_degrada_o_rate_limit_em_vez_de_virar_500()
        {
            using var limitador = new LimitadorJanelaFixaRedis(RedisQueCancelaComandos(), "tratoo:rl:teste",
                new FixedWindowRateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 },
                NullLogger.Instance);

            var resultados = new List<bool>();
            for (var i = 0; i < 3; i++)
            {
                using var lease = await limitador.AcquireAsync(1); // não lança
                resultados.Add(lease.IsAcquired);
            }

            Assert.Equal(new[] { true, true, false }, resultados);
        }

        [Fact]
        public async Task Comando_cancelado_pelo_cliente_Redis_no_estado_efemero_vira_indisponivel_e_nao_500()
        {
            var estado = new EstadoEfemeroRedis(RedisQueCancelaComandos(), NovaProtecao(), NullLogger<EstadoEfemeroRedis>.Instance);

            await Assert.ThrowsAsync<ServicoIndisponivelException>(() => estado.ObterAsync<string>("mfa:login:a@b.c"));
            await Assert.ThrowsAsync<ServicoIndisponivelException>(() => estado.DefinirAsync("mfa:login:a@b.c", "h", TimeSpan.FromMinutes(1)));
            await Assert.ThrowsAsync<ServicoIndisponivelException>(() => estado.IncrementarAsync("t", TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public async Task Redis_fora_do_ar_deixa_o_health_check_de_readiness_nao_saudavel()
        {
            using var redis = RedisInalcancavel();
            var resultado = await new RedisHealthCheck(redis).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, resultado.Status);
        }

        [Fact]
        public async Task Pedido_de_reset_de_senha_continua_identico_com_o_armazenamento_fora_do_ar()
        {
            // Anti-enumeração: se só contas existentes recebessem 503, o endpoint revelaria
            // quais e-mails estão cadastrados.
            var usuario = new Contratante { Id = 1, Nome = "Fulano", Email = "fulano@exemplo.com", Status = StatusUsuario.Active };
            var emailEnviado = false;

            var login = new LoginService(
                Falso<IUsuarioRepository>.Criar(new Dictionary<string, Func<object?[], object?>>
                {
                    [nameof(IUsuarioRepository.ObterPorEmailAsync)] = _ => Task.FromResult<Usuario?>(usuario)
                }),
                Falso<IAuditLogRepository>.Criar(),
                Falso<IEmailService>.Criar(new Dictionary<string, Func<object?[], object?>>
                {
                    [nameof(IEmailService.EnviarCodigoResetSenhaAsync)] = _ => { emailEnviado = true; return Task.CompletedTask; }
                }),
                new VerificacaoMFAService(new EstadoEfemeroRedis(RedisInalcancavel(), NovaProtecao(), NullLogger<EstadoEfemeroRedis>.Instance)),
                NullLogger<LoginService>.Instance);

            await login.SolicitarResetSenhaAsync("fulano@exemplo.com", "203.0.113.10"); // não lança

            Assert.False(emailEnviado); // nenhum código criado, nada enviado
        }

        [Fact]
        public async Task Indisponibilidade_vira_503_com_Retry_After_e_regra_de_negocio_continua_400()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            app.UseTratooTratamentoDeErros();
            app.MapGet("/indisponivel", string () => throw new ServicoIndisponivelException("Serviço temporariamente indisponível."));
            app.MapGet("/negocio", string () => throw new NegocioException("Regra violada."));
            await app.StartAsync();
            var cliente = app.GetTestClient();

            var indisponivel = await cliente.GetAsync("/indisponivel");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, indisponivel.StatusCode);
            Assert.Equal(TratamentoErrosSetup.SegundosParaNovaTentativaIndisponivel,
                indisponivel.Headers.GetValues("Retry-After").Single());
            Assert.Contains("indisponível", (await indisponivel.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["mensagem"]);

            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync("/negocio")).StatusCode);
        }
    }
}
