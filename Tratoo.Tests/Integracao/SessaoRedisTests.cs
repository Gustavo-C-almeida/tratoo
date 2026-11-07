using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Net;
using System.Text;
using Tratoo.API.Infrastructure;
using Tratoo.Tests.Sessao;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Controle de sessão contra um Redis real, com duas "réplicas" da API (dois hosts, cada
    /// um com sua conexão) — o que o host em memória de SessaoTests não prova: atomicidade do
    /// Lua entre conexões, revogação vista pela outra réplica, TTL de verdade no Redis e o
    /// comportamento com o Redis parado.
    /// </summary>
    [Collection(ColecaoRedis.Nome)]
    public class SessaoRedisTests
    {
        private readonly RedisFixture _redis;

        public SessaoRedisTests(RedisFixture redis) => _redis = redis;

        private Dictionary<string, string?> ConfigRedis(string? connectionString = null, Dictionary<string, string?>? extra = null)
        {
            var c = new Dictionary<string, string?>
            {
                [EstadoEfemeroSetup.ChaveConexao] = connectionString ?? _redis.ConnectionString,
                [ProtecaoEstadoEfemero.ChaveConfiguracao] = RedisFixture.ChaveProtecao
            };
            foreach (var (k, v) in extra ?? new()) c[k] = v;
            return c;
        }

        private static Navegador CopiarPara(HostDeSessao host, Navegador origem)
        {
            var copia = host.NovoNavegador();
            copia.DefinirCookie(SessaoAtual.NomeCookieAccess, origem.Cookie(SessaoAtual.NomeCookieAccess)!);
            copia.DefinirCookie(SessaoAtual.NomeCookieRefresh, origem.Cookie(SessaoAtual.NomeCookieRefresh)!, SessaoAtual.CaminhoCookieRefresh);
            return copia;
        }

        [FactComDocker]
        public async Task Refreshes_simultaneos_nas_duas_replicas_tem_um_unico_vencedor_e_nao_revogam()
        {
            await using var a = await HostDeSessao.SubirAsync(ConfigRedis());
            await using var b = await HostDeSessao.SubirAsync(ConfigRedis(), usuarios: a.Usuarios);
            var nav = a.NovoNavegador();
            await a.EntrarAsync(nav, a.Usuarios.Novo());
            var token = nav.Cookie(SessaoAtual.NomeCookieRefresh)!;

            var abas = Enumerable.Range(0, 12).Select(i =>
            {
                var aba = (i % 2 == 0 ? a : b).NovoNavegador();
                aba.DefinirCookie(SessaoAtual.NomeCookieRefresh, token, SessaoAtual.CaminhoCookieRefresh);
                return aba;
            }).ToList();

            var respostas = await Task.WhenAll(abas.Select(x => x.PostAsync("/api/auth/refresh")));

            Assert.Equal(1, respostas.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(11, respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.NotNull(await b.Store.ObterAsync(nav.Sid!));
            Assert.Equal(HttpStatusCode.OK, (await CopiarPara(b, nav).GetAsync("/teste/comum")).StatusCode);
        }

        [FactComDocker]
        public async Task Revogacao_numa_replica_vale_na_outra_na_requisicao_seguinte()
        {
            await using var a = await HostDeSessao.SubirAsync(ConfigRedis());
            await using var b = await HostDeSessao.SubirAsync(ConfigRedis(), usuarios: a.Usuarios);
            var nav = a.NovoNavegador();
            await a.EntrarAsync(nav, a.Usuarios.Novo());
            var naOutra = CopiarPara(b, nav);

            Assert.Equal(HttpStatusCode.OK, (await naOutra.GetAsync("/teste/comum")).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await nav.PostAsync("/api/auth/logout")).StatusCode);

            var r = await naOutra.GetAsync("/teste/comum");
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal(SessaoAutenticacaoSetup.CodigoSessaoEncerrada, await Respostas.CodigoErroAsync(r));
        }

        [FactComDocker]
        public async Task TTL_desliza_com_atividade_e_o_Redis_expira_a_sessao_por_inatividade()
        {
            // Inatividade de 3 s e registro de atividade a partir de 0,2 s: tempos curtos, sem sleeps longos.
            var config = ConfigRedis(extra: new()
            {
                ["Sessao:InatividadeMinutos"] = "0.05",
                ["Sessao:IntervaloRegistroAtividadeSegundos"] = "0.2"
            });
            await using var a = await HostDeSessao.SubirAsync(config);
            var nav = a.NovoNavegador();
            await a.EntrarAsync(nav, a.Usuarios.Novo());

            var nome = a.App.Services.GetRequiredService<ProtecaoEstadoEfemero>().NomeDaChave($"sessao:{nav.Sid}");
            var db = _redis.Bruto.GetDatabase();
            Assert.InRange((await db.KeyTimeToLiveAsync(nome))!.Value, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

            await Task.Delay(1800);
            Assert.True((await db.KeyTimeToLiveAsync(nome))!.Value < TimeSpan.FromSeconds(1.5));

            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum")).StatusCode);
            Assert.True((await db.KeyTimeToLiveAsync(nome))!.Value > TimeSpan.FromSeconds(2));   // renovado

            // Passiva não renova: o Redis expira a chave sozinho.
            await Task.Delay(1500);
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum", passivo: true)).StatusCode);
            await Task.Delay(2000);
            Assert.False(await db.KeyExistsAsync(nome));
            Assert.Equal(HttpStatusCode.Unauthorized, (await nav.GetAsync("/teste/comum")).StatusCode);
        }

        [FactComDocker]
        public async Task SET_XX_nao_ressuscita_sessao_revogada()
        {
            await using var a = await HostDeSessao.SubirAsync(ConfigRedis());
            await using var b = await HostDeSessao.SubirAsync(ConfigRedis(), usuarios: a.Usuarios);
            var nav = a.NovoNavegador();
            await a.EntrarAsync(nav, a.Usuarios.Novo());
            var sid = nav.Sid!;

            // A réplica A leu a sessão (validação da requisição) ...
            var lida = (await a.Store.ObterAsync(sid))!;
            // ... a B revoga ...
            Assert.True(await b.Store.RemoverAsync(sid, lida.UsuarioId));
            // ... e a A tenta registrar a atividade com o que tinha lido.
            var config = a.App.Services.GetRequiredService<SessaoConfig>();
            var depois = await SessaoService.RegistrarAtividadeAsync(a.Store, config, sid, lida, lida.UltimaAtividadeEm.AddMinutes(5));

            Assert.Equal(lida, depois);   // nada gravado
            Assert.Null(await b.Store.ObterAsync(sid));
            Assert.False(await a.Store.AtualizarSeExisteAsync(sid, lida, TimeSpan.FromMinutes(30)));
            var nome = a.App.Services.GetRequiredService<ProtecaoEstadoEfemero>().NomeDaChave($"sessao:{sid}");
            Assert.False(await _redis.Bruto.GetDatabase().KeyExistsAsync(nome));
        }

        [FactComDocker]
        public async Task Nada_da_sessao_fica_em_claro_no_Redis()
        {
            await using var a = await HostDeSessao.SubirAsync(ConfigRedis());
            var usuario = a.Usuarios.Novo();
            var nav = a.NovoNavegador("Mozilla/5.0 (Marcador-Unico-De-Teste)");
            await a.EntrarAsync(nav, usuario);
            var (sid, segredo) = (nav.Cookie(SessaoAtual.NomeCookieRefresh)!.Split('.')[0], nav.Cookie(SessaoAtual.NomeCookieRefresh)!.Split('.')[1]);

            var servidor = _redis.Bruto.GetServer(_redis.Bruto.GetEndPoints().Single());
            var db = _redis.Bruto.GetDatabase();
            var chaves = new[] { "sessao", "refresh", "sessoes-usuario" }
                .SelectMany(c => servidor.Keys(pattern: $"tratoo:{c}:*").Select(k => k.ToString()))
                .ToList();
            Assert.NotEmpty(chaves);
            Assert.DoesNotContain(chaves, k => k.Contains(sid, StringComparison.Ordinal) || k.Contains(segredo, StringComparison.Ordinal));

            var protecao = a.App.Services.GetRequiredService<ProtecaoEstadoEfemero>();
            var sessaoBruta = Encoding.Latin1.GetString((byte[])(await db.StringGetAsync(protecao.NomeDaChave($"sessao:{sid}")))!);
            Assert.DoesNotContain("Marcador-Unico", sessaoBruta);
            Assert.DoesNotContain(usuario.Id.ToString() + ",", sessaoBruta);

            var membros = await db.SetMembersAsync(protecao.NomeDaChave($"sessoes-usuario:{usuario.Id}"));
            Assert.Single(membros);
            Assert.DoesNotContain(sid, Encoding.Latin1.GetString((byte[])membros[0]!));
        }

        [FactComDocker]
        public async Task Com_o_Redis_parado_rota_sensivel_responde_503_e_rota_comum_aceita_o_JWT()
        {
            // Redis próprio: este teste derruba o servidor.
            const string senha = "teste_redis_parado";
            await using var container = ContainerDocker.Iniciar("redis:7-alpine", 6379,
                comando: new[] { "redis-server", "--requirepass", senha, "--save", "", "--appendonly", "no" });
            var conexao = $"127.0.0.1:{container.Porta},password={senha},abortConnect=false,connectTimeout=2000,asyncTimeout=2000";
            await EsperarRedisAsync(conexao);

            await using var host = await HostDeSessao.SubirAsync(ConfigRedis(conexao));
            var nav = host.NovoNavegador();
            await host.EntrarAsync(nav, host.Usuarios.Novo());
            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/sensivel")).StatusCode);

            container.Parar();

            var sensivel = await nav.GetAsync("/teste/sensivel");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, sensivel.StatusCode);
            Assert.Equal("5", sensivel.Headers.RetryAfter?.ToString());
            Assert.Equal(SessaoAutenticacaoSetup.CodigoSessaoIndisponivel, await Respostas.CodigoErroAsync(sensivel));

            Assert.Equal(HttpStatusCode.OK, (await nav.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await nav.PostAsync("/api/auth/refresh")).StatusCode);
        }

        private static async Task EsperarRedisAsync(string conexao)
        {
            var limite = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                try
                {
                    await using var c = await ConnectionMultiplexer.ConnectAsync(conexao.Replace("abortConnect=false", "abortConnect=true"));
                    await c.GetDatabase().PingAsync();
                    return;
                }
                catch when (DateTime.UtcNow < limite)
                {
                    await Task.Delay(300);
                }
            }
        }
    }
}
