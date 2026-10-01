using StackExchange.Redis;
using System.Text.Json;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Infrastructure;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// <see cref="IEstadoEfemero"/> no Redis: o mesmo estado para todas as réplicas — um
    /// código gerado na réplica A vale na B, e o limite de tentativas é global.
    ///
    /// Conteúdo protegido por <see cref="ProtecaoEstadoEfemero"/> (valores cifrados, nomes
    /// de chave em HMAC). Contadores ficam em claro: o Redis só incrementa inteiros, e um
    /// número de tentativas não revela nada sem o nome da chave.
    ///
    /// Falha do Redis → <see cref="ServicoIndisponivelException"/> (503). Nunca cai para a
    /// memória local: isso reintroduziria, em silêncio, o código que não vale na outra
    /// réplica e o limite de tentativas dividido por N.
    /// </summary>
    public sealed class EstadoEfemeroRedis : IEstadoEfemero
    {
        private const string MensagemIndisponivel =
            "Serviço temporariamente indisponível. Tente novamente em instantes.";

        // INCR + renovação do TTL numa operação só (atômica no Redis).
        private const string ScriptIncrementar =
            "local n = redis.call('INCR', KEYS[1]) " +
            "redis.call('PEXPIRE', KEYS[1], ARGV[1]) " +
            "return n";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IConnectionMultiplexer _redis;
        private readonly ProtecaoEstadoEfemero _protecao;
        private readonly ILogger<EstadoEfemeroRedis> _logger;

        public EstadoEfemeroRedis(
            IConnectionMultiplexer redis,
            ProtecaoEstadoEfemero protecao,
            ILogger<EstadoEfemeroRedis> logger)
        {
            _redis = redis;
            _protecao = protecao;
            _logger = logger;
        }

        public Task DefinirAsync<T>(string chave, T valor, TimeSpan ttl) =>
            ExecutarAsync(db =>
            {
                var nome = _protecao.NomeDaChave(chave);
                return db.StringSetAsync(nome, Cifrar(valor, nome), ttl);
            });

        public Task<bool> DefinirSeAusenteAsync<T>(string chave, T valor, TimeSpan ttl) =>
            ExecutarAsync(db =>
            {
                var nome = _protecao.NomeDaChave(chave);
                return db.StringSetAsync(nome, Cifrar(valor, nome), ttl, When.NotExists);
            });

        public Task<T?> ObterAsync<T>(string chave) =>
            ExecutarAsync<T?>(async db =>
            {
                var nome = _protecao.NomeDaChave(chave);
                var valor = await db.StringGetAsync(nome);
                if (valor.IsNullOrEmpty)
                    return default;

                var claro = _protecao.Decifrar((byte[])valor!, nome);
                if (claro is null)
                {
                    // Segredo trocado ou valor adulterado: ilegível = ausente.
                    _logger.LogWarning("Valor ilegível em {Categoria}; tratado como ausente.", nome.Split(':')[1]);
                    return default;
                }

                return JsonSerializer.Deserialize<T>(claro, Json);
            });

        public Task RemoverAsync(params string[] chaves) =>
            ExecutarAsync(db => db.KeyDeleteAsync(chaves.Select(c => (RedisKey)_protecao.NomeDaChave(c)).ToArray()));

        public Task<long> IncrementarAsync(string chave, TimeSpan ttl) =>
            ExecutarAsync(async db =>
            {
                var resultado = await db.ScriptEvaluateAsync(
                    ScriptIncrementar,
                    new RedisKey[] { _protecao.NomeDaChave(chave) },
                    new RedisValue[] { (long)ttl.TotalMilliseconds });
                return (long)resultado;
            });

        private byte[] Cifrar<T>(T valor, string nome) =>
            _protecao.Cifrar(JsonSerializer.SerializeToUtf8Bytes(valor, Json), nome);

        private async Task<TResultado> ExecutarAsync<TResultado>(Func<IDatabase, Task<TResultado>> operacao)
        {
            try
            {
                return await operacao(_redis.GetDatabase());
            }
            // TaskCanceledException: com BacklogPolicy.FailFast o cliente cancela os comandos
            // pendentes quando a conexão oscila. Também é "Redis indisponível" (503), não 500.
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException or TaskCanceledException)
            {
                _logger.LogError(ex, "Redis indisponível para estado efêmero (OTP/tentativas/cadastro). Respondendo 503.");
                throw new ServicoIndisponivelException(MensagemIndisponivel, ex);
            }
        }
    }
}
