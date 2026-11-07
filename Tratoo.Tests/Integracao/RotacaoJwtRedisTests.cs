using System.Net;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Features.Auth;
using Tratoo.Tests.Sessao;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Troca da chave do JWT com sessões num Redis real, compartilhado por "réplicas" com
    /// configurações diferentes, como durante um deploy: a troca não derruba nenhuma sessão.
    /// </summary>
    [Collection(ColecaoRedis.Nome)]
    public class RotacaoJwtRedisTests
    {
        private const string K1 = "chave-jwt-antiga-da-rotacao-redis-0123456789";
        private const string K2 = "chave-jwt-nova-da-rotacao-redis-0123456789ab";

        private readonly RedisFixture _redis;

        public RotacaoJwtRedisTests(RedisFixture redis) => _redis = redis;

        private Dictionary<string, string?> Config(string atual, string? anterior = null) => new()
        {
            [EstadoEfemeroSetup.ChaveConexao] = _redis.ConnectionString,
            [ProtecaoEstadoEfemero.ChaveConfiguracao] = RedisFixture.ChaveProtecao,
            [ChavesJwt.ChaveConfiguracao] = atual,
            [ChavesJwt.ChaveAnteriorConfiguracao] = anterior
        };

        private static Navegador CopiarPara(HostDeSessao host, Navegador origem)
        {
            var copia = host.NovoNavegador();
            copia.DefinirCookie(SessaoAtual.NomeCookieAccess, origem.Cookie(SessaoAtual.NomeCookieAccess)!);
            copia.DefinirCookie(SessaoAtual.NomeCookieRefresh, origem.Cookie(SessaoAtual.NomeCookieRefresh)!, SessaoAtual.CaminhoCookieRefresh);
            return copia;
        }

        [FactComDocker]
        public async Task Com_a_anterior_configurada_o_access_antigo_vale_sem_refresh()
        {
            await using var antiga = await HostDeSessao.SubirAsync(Config(K1));
            await using var nova = await HostDeSessao.SubirAsync(Config(K2, anterior: K1), usuarios: antiga.Usuarios);
            var nav = antiga.NovoNavegador();
            await antiga.EntrarAsync(nav, antiga.Usuarios.Novo());

            var naNova = CopiarPara(nova, nav);
            Assert.Equal(HttpStatusCode.OK, (await naNova.GetAsync("/teste/comum")).StatusCode);
        }

        [FactComDocker]
        public async Task Sem_a_anterior_o_access_antigo_leva_401_mas_o_refresh_mantem_a_mesma_sessao()
        {
            await using var antiga = await HostDeSessao.SubirAsync(Config(K1));
            await using var nova = await HostDeSessao.SubirAsync(Config(K2), usuarios: antiga.Usuarios);
            var nav = antiga.NovoNavegador();
            await antiga.EntrarAsync(nav, antiga.Usuarios.Novo());
            var sid = nav.Sid;

            var naNova = CopiarPara(nova, nav);
            Assert.Equal(HttpStatusCode.Unauthorized, (await naNova.GetAsync("/teste/comum")).StatusCode);

            // É o que o api.js faz num 401: refresh e repete. A sessão é a mesma: ninguém deslogou.
            Assert.Equal(HttpStatusCode.OK, (await naNova.PostAsync("/api/auth/refresh")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await naNova.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(sid, naNova.Sid);
            Assert.NotNull(await nova.Store.ObterAsync(sid!));
        }

        [FactComDocker]
        public async Task Durante_o_deploy_replicas_antiga_e_nova_aceitam_os_tokens_uma_da_outra()
        {
            // Réplica antiga: K1 só. Réplica nova: assina K2 e valida K1. O token novo não
            // vale na antiga, mas o refresh nela devolve um token K1, que vale nas duas.
            await using var antiga = await HostDeSessao.SubirAsync(Config(K1));
            await using var nova = await HostDeSessao.SubirAsync(Config(K2, anterior: K1), usuarios: antiga.Usuarios);
            var nav = nova.NovoNavegador();
            await nova.EntrarAsync(nav, nova.Usuarios.Novo());
            var sid = nav.Sid;

            var naAntiga = CopiarPara(antiga, nav);
            Assert.Equal(HttpStatusCode.Unauthorized, (await naAntiga.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await naAntiga.PostAsync("/api/auth/refresh")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await naAntiga.GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await CopiarPara(nova, naAntiga).GetAsync("/teste/comum")).StatusCode);
            Assert.Equal(sid, naAntiga.Sid);
        }
    }
}
