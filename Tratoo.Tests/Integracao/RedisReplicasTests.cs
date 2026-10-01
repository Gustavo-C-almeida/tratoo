using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Infrastructure;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Duas "réplicas" da API (containers de DI independentes, cada um com sua conexão)
    /// compartilhando um Redis real — o que muda quando a API deixa de ter uma réplica só.
    /// </summary>
    [Collection(ColecaoRedis.Nome)]
    public class RedisReplicasTests
    {
        private readonly RedisFixture _redis;

        public RedisReplicasTests(RedisFixture redis) => _redis = redis;

        private static string EmailUnico() => $"pessoa.{Guid.NewGuid():N}@exemplo.com";

        [FactComDocker]
        public async Task Codigo_gerado_numa_replica_vale_na_outra_e_uma_unica_vez()
        {
            await using var replicaA = _redis.NovaReplica();
            await using var replicaB = _redis.NovaReplica();
            var email = EmailUnico();

            var codigo = await replicaA.GetRequiredService<IVerificacaoMFAService>().GerarECriarAsync(email, "login");

            await replicaB.GetRequiredService<IVerificacaoMFAService>().ValidarAsync(email, codigo, "login");

            // Consumido na B, não vale mais na A.
            await Assert.ThrowsAsync<NegocioException>(() =>
                replicaA.GetRequiredService<IVerificacaoMFAService>().ValidarAsync(email, codigo, "login"));
        }

        [FactComDocker]
        public async Task Limite_de_tentativas_e_da_aplicacao_e_nao_de_cada_replica()
        {
            await using var replicaA = _redis.NovaReplica();
            await using var replicaB = _redis.NovaReplica();
            var mfaA = replicaA.GetRequiredService<IVerificacaoMFAService>();
            var mfaB = replicaB.GetRequiredService<IVerificacaoMFAService>();
            var email = EmailUnico();
            var codigo = await mfaA.GerarECriarAsync(email, "login");

            // Erros alternando de réplica: em memória, cada uma contaria só a metade.
            for (var i = 1; i < VerificacaoMFAService.MaxTentativas; i++)
            {
                var mfa = i % 2 == 0 ? mfaA : mfaB;
                var erro = await Assert.ThrowsAsync<NegocioException>(() => mfa.ValidarAsync(email, "000000", "login"));
                Assert.Equal("Código inválido", erro.Message);
            }

            var bloqueio = await Assert.ThrowsAsync<NegocioException>(() => mfaB.ValidarAsync(email, "000000", "login"));
            Assert.Contains("Muitas tentativas", bloqueio.Message);
            await Assert.ThrowsAsync<NegocioException>(() => mfaA.ValidarAsync(email, codigo, "login"));
        }

        [FactComDocker]
        public async Task Cadastro_pendente_trafega_entre_replicas_e_nada_fica_em_claro_no_Redis()
        {
            await using var replicaA = _redis.NovaReplica();
            await using var replicaB = _redis.NovaReplica();
            var email = EmailUnico();
            var dados = new DadosCadastroPendente(
                Nome: "Fulana de Tal", Email: email, Tipo: "Prestador", MFA: true,
                SenhaHash: "$2a$11$hashbcryptdeexemplo", Ip: "203.0.113.50", AceitouTermos: true);

            await replicaA.GetRequiredService<IEstadoEfemero>().DefinirAsync($"cadastro:{email}", dados, TimeSpan.FromHours(24));
            var lido = await replicaB.GetRequiredService<IEstadoEfemero>().ObterAsync<DadosCadastroPendente>($"cadastro:{email}");

            Assert.Equal(dados, lido);

            // O que um curioso com acesso ao Redis veria: nem o e-mail na chave, nem os
            // dados no valor. Só a categoria e o TTL.
            var servidor = _redis.Bruto.GetServer(_redis.Bruto.GetEndPoints().Single());
            var chaves = servidor.Keys(pattern: "tratoo:cadastro:*").Select(k => k.ToString()).ToList();
            Assert.NotEmpty(chaves);
            Assert.DoesNotContain(chaves, k => k.Contains("pessoa.", StringComparison.Ordinal));

            var nome = replicaA.GetRequiredService<ProtecaoEstadoEfemero>().NomeDaChave($"cadastro:{email}");
            var bruto = Encoding.Latin1.GetString((byte[])(await _redis.Bruto.GetDatabase().StringGetAsync(nome))!);
            foreach (var trecho in new[] { email, "Fulana", "hashbcrypt", "203.0.113.50" })
                Assert.DoesNotContain(trecho, bruto);

            var ttl = await _redis.Bruto.GetDatabase().KeyTimeToLiveAsync(nome);
            Assert.InRange(ttl!.Value, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24));
        }

        [FactComDocker]
        public async Task Incremento_e_atomico_entre_replicas()
        {
            await using var replicaA = _redis.NovaReplica();
            await using var replicaB = _redis.NovaReplica();
            var chave = $"mfa-tentativas:login:{EmailUnico()}";
            var estados = new[] { replicaA.GetRequiredService<IEstadoEfemero>(), replicaB.GetRequiredService<IEstadoEfemero>() };

            var resultados = await Task.WhenAll(Enumerable.Range(0, 100)
                .Select(i => estados[i % 2].IncrementarAsync(chave, TimeSpan.FromMinutes(5))));

            Assert.Equal(Enumerable.Range(1, 100).Select(i => (long)i), resultados.OrderBy(x => x));
        }

        [FactComDocker]
        public async Task Marca_de_envio_so_e_obtida_por_uma_replica()
        {
            await using var replicaA = _redis.NovaReplica();
            await using var replicaB = _redis.NovaReplica();
            var chave = $"lembrete-avaliacao-d3:{Guid.NewGuid()}";
            var estados = new[] { replicaA.GetRequiredService<IEstadoEfemero>(), replicaB.GetRequiredService<IEstadoEfemero>() };

            var vencedores = await Task.WhenAll(Enumerable.Range(0, 20)
                .Select(i => estados[i % 2].DefinirSeAusenteAsync(chave, true, TimeSpan.FromDays(2))));

            Assert.Equal(1, vencedores.Count(v => v));
        }

        [FactComDocker]
        public async Task Rate_limit_e_da_aplicacao_e_nao_de_cada_replica()
        {
            await using var replicaA = _redis.NovaReplica();
            await using var replicaB = _redis.NovaReplica();
            // Janela de 1 h para o teste não cruzar a virada da janela no meio.
            var opcoes = new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromHours(1), QueueLimit = 0 };
            var chave = $"tratoo:rl:teste:{Guid.NewGuid():N}";

            using var limitadorA = new LimitadorJanelaFixaRedis(
                replicaA.GetRequiredService<IConnectionMultiplexer>(), chave, opcoes, NullLogger.Instance);
            using var limitadorB = new LimitadorJanelaFixaRedis(
                replicaB.GetRequiredService<IConnectionMultiplexer>(), chave, opcoes, NullLogger.Instance);

            var obtidos = 0;
            for (var i = 0; i < 8; i++)
            {
                using var lease = await (i % 2 == 0 ? limitadorA : limitadorB).AcquireAsync(1);
                if (lease.IsAcquired) obtidos++;
            }

            // Em memória seriam 3 por réplica = 6.
            Assert.Equal(3, obtidos);
        }

        /// <summary>
        /// Duas APIs (pipeline HTTP real: UseRateLimiter + política "login" de produção)
        /// apontando para o mesmo Redis. Prova a ligação inteira — política → Redis —, não só
        /// o limitador isolado.
        /// </summary>
        private static async Task<WebApplication> SubirReplicaAsync(Dictionary<string, string?> config)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(config);
            builder.Services.AddTratooEstadoEfemero(builder.Configuration);
            builder.Services.AddTratooRateLimiter();

            var app = builder.Build();
            app.UseRateLimiter();
            app.MapPost("/login", () => Results.Ok()).RequireRateLimiting(RateLimiterSetup.PoliticaLogin);
            await app.StartAsync();
            return app;
        }

        private static async Task<(int Ok, int Bloqueadas)> DispararAlternandoAsync(WebApplication a, WebApplication b, int total)
        {
            var clientes = new[] { a.GetTestClient(), b.GetTestClient() };
            var ok = 0; var bloqueadas = 0;
            for (var i = 0; i < total; i++)
            {
                var resposta = await clientes[i % 2].PostAsync("/login", null);
                if (resposta.StatusCode == HttpStatusCode.TooManyRequests) bloqueadas++;
                else if (resposta.IsSuccessStatusCode) ok++;
            }
            return (ok, bloqueadas);
        }

        [FactComDocker]
        public async Task Politica_de_login_conta_as_duas_replicas_juntas_quando_ha_Redis()
        {
            // Limpa o contador desta janela (outros testes/execuções podem ter usado o mesmo IP).
            await _redis.Bruto.GetDatabase().ExecuteAsync("FLUSHDB");

            var comRedis = new Dictionary<string, string?>
            {
                [EstadoEfemeroSetup.ChaveConexao] = _redis.ConnectionString,
                [ProtecaoEstadoEfemero.ChaveConfiguracao] = RedisFixture.ChaveProtecao
            };
            await using var replicaA = await SubirReplicaAsync(comRedis);
            await using var replicaB = await SubirReplicaAsync(comRedis);

            var (ok, bloqueadas) = await DispararAlternandoAsync(replicaA, replicaB, 14);

            Assert.Equal(10, ok);          // o limite da política login, para a aplicação inteira
            Assert.Equal(4, bloqueadas);
        }

        [Fact]
        public async Task Sem_Redis_cada_replica_tem_seu_proprio_limite()
        {
            // Controle (comportamento de produção hoje, 1 réplica): com 2 réplicas em
            // memória, 14 tentativas passam todas — 7 em cada, abaixo de 10 por réplica.
            await using var replicaA = await SubirReplicaAsync(new Dictionary<string, string?>());
            await using var replicaB = await SubirReplicaAsync(new Dictionary<string, string?>());

            var (ok, bloqueadas) = await DispararAlternandoAsync(replicaA, replicaB, 14);

            Assert.Equal(14, ok);
            Assert.Equal(0, bloqueadas);
        }

        [FactComDocker]
        public async Task Health_check_fica_saudavel_com_o_redis_no_ar()
        {
            await using var replica = _redis.NovaReplica();
            var resultado = await new RedisHealthCheck(replica.GetRequiredService<IConnectionMultiplexer>())
                .CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, resultado.Status);
        }
    }
}
