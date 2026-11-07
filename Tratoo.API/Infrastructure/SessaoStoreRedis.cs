using StackExchange.Redis;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Tratoo.Domain.Exceptions;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// <see cref="ISessaoStore"/> no Redis. Tudo passa por <see cref="ProtecaoEstadoEfemero"/>:
    /// nomes de chave em HMAC (o refresh token, o sid e o id do usuário nunca aparecem num
    /// nome) e valores em AES-256-GCM, inclusive os membros do índice
    /// <c>sessoes-usuario:{id}</c>. O cifrado tem nonce aleatório, então um membro só é
    /// removido pelo valor bruto lido — por isso <see cref="RemoverAsync"/> lê o índice antes.
    ///
    /// Operações compostas são um comando único ou um script Lua (atômicos no Redis), no
    /// padrão de <c>EstadoEfemeroRedis.RemoverSeIgualAsync</c>.
    ///
    /// Falha do Redis (inclusive <see cref="TaskCanceledException"/>, que o
    /// <c>BacklogPolicy.FailFast</c> gera quando a conexão oscila) →
    /// <see cref="ServicoIndisponivelException"/>, com no máximo um aviso no log a cada 30 s.
    /// </summary>
    public sealed class SessaoStoreRedis : ISessaoStore
    {
        private const string MensagemIndisponivel =
            "Serviço de sessão temporariamente indisponível. Tente novamente em instantes.";

        // Sessão + refresh + índice numa operação só. O TTL do índice só cresce: ele vive
        // enquanto a sessão mais longa do usuário puder viver (PTTL -1 = sem TTL ainda).
        private const string ScriptCriar =
            "redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2]) " +
            "redis.call('SET', KEYS[2], ARGV[3], 'PX', ARGV[4]) " +
            "redis.call('SADD', KEYS[3], ARGV[5]) " +
            "local t = redis.call('PTTL', KEYS[3]) " +
            "if t < tonumber(ARGV[6]) then redis.call('PEXPIRE', KEYS[3], ARGV[6]) end " +
            "return 1";

        // Consumir o refresh e gravar a marca de uso na MESMA operação: só quem apagar vence,
        // e quem chegar depois sempre encontra a marca (nunca "token sumiu, marca ausente").
        private const string ScriptConsumirRefresh =
            "if redis.call('GET', KEYS[1]) == ARGV[1] then " +
            "redis.call('DEL', KEYS[1]) " +
            "redis.call('SET', KEYS[2], ARGV[2], 'PX', ARGV[3]) " +
            "return 1 end " +
            "return 0";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private static long _ultimoAvisoTicks;

        private readonly IConnectionMultiplexer _redis;
        private readonly ProtecaoEstadoEfemero _protecao;
        private readonly ILogger<SessaoStoreRedis> _logger;

        public SessaoStoreRedis(IConnectionMultiplexer redis, ProtecaoEstadoEfemero protecao, ILogger<SessaoStoreRedis> logger)
        {
            _redis = redis;
            _protecao = protecao;
            _logger = logger;
        }

        public Task CriarAsync(string sid, DadosSessao sessao, TimeSpan ttlSessao, string refreshToken, TimeSpan ttlRefresh) =>
            ExecutarAsync(async db =>
            {
                var nomeSessao = _protecao.NomeDaChave(ChavesSessao.Sessao(sid));
                var nomeRefresh = _protecao.NomeDaChave(ChavesSessao.Refresh(refreshToken));
                var nomeIndice = _protecao.NomeDaChave(ChavesSessao.Indice(sessao.UsuarioId));

                await db.ScriptEvaluateAsync(ScriptCriar,
                    new RedisKey[] { nomeSessao, nomeRefresh, nomeIndice },
                    new RedisValue[]
                    {
                        CifrarJson(sessao, nomeSessao), Ms(ttlSessao),
                        CifrarJson(sid, nomeRefresh), Ms(ttlRefresh),
                        _protecao.Cifrar(Encoding.UTF8.GetBytes(sid), nomeIndice), Ms(ttlRefresh)
                    });
                return true;
            });

        public Task<DadosSessao?> ObterAsync(string sid) =>
            ExecutarAsync(async db =>
            {
                var nome = _protecao.NomeDaChave(ChavesSessao.Sessao(sid));
                return (await LerAsync<DadosSessao>(db, nome)).Valor;
            });

        public Task<bool> AtualizarSeExisteAsync(string sid, DadosSessao sessao, TimeSpan ttl) =>
            ExecutarAsync(db =>
            {
                var nome = _protecao.NomeDaChave(ChavesSessao.Sessao(sid));
                // SET ... PX ... XX: um comando só. Se a sessão foi revogada depois da leitura,
                // a chave não existe mais e nada é gravado.
                return db.StringSetAsync(nome, CifrarJson(sessao, nome), ttl, When.Exists);
            });

        public Task<bool> RemoverAsync(string sid, int usuarioId) =>
            ExecutarAsync(async db =>
            {
                var nomeIndice = _protecao.NomeDaChave(ChavesSessao.Indice(usuarioId));
                var membros = await db.SetMembersAsync(nomeIndice);
                var doSid = membros.Where(m => DecifrarMembro(m, nomeIndice) == sid).ToArray();

                var tran = db.CreateTransaction();
                var apagou = tran.KeyDeleteAsync(_protecao.NomeDaChave(ChavesSessao.Sessao(sid)));
                _ = tran.KeyDeleteAsync(_protecao.NomeDaChave(ChavesSessao.Reauth(sid)));
                if (doSid.Length > 0)
                    _ = tran.SetRemoveAsync(nomeIndice, doSid);
                await tran.ExecuteAsync();
                return await apagou;
            });

        public Task<IReadOnlyList<(string Sid, DadosSessao Sessao)>> ListarDoUsuarioAsync(int usuarioId) =>
            ExecutarAsync<IReadOnlyList<(string, DadosSessao)>>(async db =>
            {
                var nomeIndice = _protecao.NomeDaChave(ChavesSessao.Indice(usuarioId));
                var membros = await db.SetMembersAsync(nomeIndice);

                var vivas = new List<(string, DadosSessao)>();
                var orfaos = new List<RedisValue>();

                foreach (var membro in membros)
                {
                    var sid = DecifrarMembro(membro, nomeIndice);
                    if (sid is null) { orfaos.Add(membro); continue; }

                    var sessao = (await LerAsync<DadosSessao>(db, _protecao.NomeDaChave(ChavesSessao.Sessao(sid)))).Valor;
                    if (sessao is null || sessao.UsuarioId != usuarioId) { orfaos.Add(membro); continue; }

                    vivas.Add((sid, sessao));
                }

                // Sessões expiradas pelo próprio Redis deixam o sid no índice: limpa aqui.
                if (orfaos.Count > 0)
                    await db.SetRemoveAsync(nomeIndice, orfaos.ToArray());

                return vivas;
            });

        public Task<string?> ObterSidDoRefreshAsync(string refreshToken) =>
            ExecutarAsync(async db =>
                (await LerAsync<string>(db, _protecao.NomeDaChave(ChavesSessao.Refresh(refreshToken)))).Valor);

        public Task<bool> ConsumirRefreshAsync(string refreshToken, string sid, RefreshUsado marca, TimeSpan ttlMarca) =>
            ExecutarAsync(async db =>
            {
                var nome = _protecao.NomeDaChave(ChavesSessao.Refresh(refreshToken));
                var (bruto, valor) = await LerAsync<string>(db, nome);
                if (bruto.IsNullOrEmpty || valor != sid)
                    return false;

                var nomeUsado = _protecao.NomeDaChave(ChavesSessao.RefreshUsado(refreshToken));
                // Compara o CIFRADO lido (nonce aleatório): um valor regravado no meio-tempo
                // não é apagado. Entre N chamadas simultâneas, só uma recebe 1.
                var r = await db.ScriptEvaluateAsync(ScriptConsumirRefresh,
                    new RedisKey[] { nome, nomeUsado },
                    new RedisValue[] { bruto, CifrarJson(marca, nomeUsado), Ms(ttlMarca) });
                return (long)r == 1;
            });

        public Task GravarRefreshAsync(string refreshToken, string sid, TimeSpan ttl) =>
            ExecutarAsync(db =>
            {
                var nome = _protecao.NomeDaChave(ChavesSessao.Refresh(refreshToken));
                return db.StringSetAsync(nome, CifrarJson(sid, nome), ttl);
            });

        public Task<RefreshUsado?> ObterRefreshUsadoAsync(string refreshToken) =>
            ExecutarAsync(async db =>
                (await LerAsync<RefreshUsado>(db, _protecao.NomeDaChave(ChavesSessao.RefreshUsado(refreshToken)))).Valor);

        public Task MarcarReautenticacaoAsync(string sid, TimeSpan ttl) =>
            ExecutarAsync(db =>
            {
                var nome = _protecao.NomeDaChave(ChavesSessao.Reauth(sid));
                return db.StringSetAsync(nome, CifrarJson(true, nome), ttl);
            });

        public Task<bool> ReautenticouRecentementeAsync(string sid) =>
            ExecutarAsync(async db =>
                (await LerAsync<bool>(db, _protecao.NomeDaChave(ChavesSessao.Reauth(sid)))).Valor);

        // ── Auxiliares ──────────────────────────────────────────────────────────

        private static long Ms(TimeSpan ttl) => Math.Max(1, (long)Math.Ceiling(ttl.TotalMilliseconds));

        private byte[] CifrarJson<T>(T valor, string nome) =>
            _protecao.Cifrar(JsonSerializer.SerializeToUtf8Bytes(valor, Json), nome);

        private string? DecifrarMembro(RedisValue membro, string nomeIndice)
        {
            if (membro.IsNullOrEmpty) return null;
            var claro = _protecao.Decifrar((byte[])membro!, nomeIndice);
            return claro is null ? null : Encoding.UTF8.GetString(claro);
        }

        private async Task<(RedisValue Bruto, T? Valor)> LerAsync<T>(IDatabase db, string nome)
        {
            var bruto = await db.StringGetAsync(nome);
            if (bruto.IsNullOrEmpty)
                return (bruto, default);

            var claro = _protecao.Decifrar((byte[])bruto!, nome);
            if (claro is null)
            {
                // Segredo trocado (Redis:ChaveProtecao) ou valor adulterado: ilegível = ausente,
                // ou seja, sessão encerrada. Nada da chave no log.
                _logger.LogWarning("Valor ilegível no armazenamento de sessão; tratado como ausente.");
                return (RedisValue.Null, default);
            }

            try
            {
                return (bruto, JsonSerializer.Deserialize<T>(claro, Json));
            }
            catch (JsonException)
            {
                _logger.LogWarning("Valor de sessão em formato inesperado; tratado como ausente.");
                return (RedisValue.Null, default);
            }
        }

        private async Task<TResultado> ExecutarAsync<TResultado>(Func<IDatabase, Task<TResultado>> operacao)
        {
            try
            {
                return await operacao(_redis.GetDatabase());
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException or TaskCanceledException)
            {
                AvisarIndisponibilidade(ex);
                throw new ServicoIndisponivelException(MensagemIndisponivel, ex);
            }
        }

        /// <summary>No máximo um aviso a cada 30 s por processo (toda requisição autenticada passa aqui).</summary>
        private void AvisarIndisponibilidade(Exception ex)
        {
            var agora = Stopwatch.GetTimestamp();
            var ultimo = Interlocked.Read(ref _ultimoAvisoTicks);
            if (ultimo != 0 && Stopwatch.GetElapsedTime(ultimo, agora) < TimeSpan.FromSeconds(30))
                return;
            if (Interlocked.CompareExchange(ref _ultimoAvisoTicks, agora, ultimo) == ultimo)
                _logger.LogWarning(ex,
                    "Redis indisponível para sessões: rotas sensíveis respondem 503; as demais aceitam só o JWT até ele voltar.");
        }
    }
}
