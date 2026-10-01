using StackExchange.Redis;
using System.Diagnostics;
using System.Threading.RateLimiting;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Janela fixa com o contador no Redis: o limite vale para a aplicação inteira, não por
    /// réplica (com 2 réplicas em memória, "10 logins/min por IP" virava 20).
    ///
    /// Janela pelo relógio do PRÓPRIO Redis (comando TIME no script), alinhada ao minuto:
    /// réplicas com relógios ligeiramente diferentes contam no mesmo balde. Diferença em
    /// relação ao limitador em memória do .NET: lá a janela começa quando o limitador é
    /// criado; aqui, no minuto cheio. O Retry-After de 60 s continua sendo teto válido.
    ///
    /// Redis fora do ar → DEGRADA para um limitador em memória por réplica (com aviso no
    /// log), em vez de bloquear: negar todo login porque o Redis caiu seria uma negação de
    /// serviço autoinfligida, e o limite local ainda protege — fica N× mais frouxo, que é
    /// exatamente o comportamento de antes desta mudança. Decisão em
    /// Docs/TRILHA2-DECISOES.md, seção 3.3.
    /// </summary>
    public sealed class LimitadorJanelaFixaRedis : RateLimiter
    {
        // INCRBY na chave da janela corrente; TTL só na criação (janela + 1 s de folga).
        private const string Script =
            "local t = redis.call('TIME') " +
            "local agora = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000) " +
            "local janela = tonumber(ARGV[1]) " +
            "local chave = KEYS[1] .. ':' .. string.format('%.0f', agora - (agora % janela)) " +
            "local n = redis.call('INCRBY', chave, tonumber(ARGV[2])) " +
            "if n == tonumber(ARGV[2]) then redis.call('PEXPIRE', chave, janela + 1000) end " +
            "return n";

        // Evita inundar o log: no máximo um aviso de degradação a cada 30 s por processo.
        private static long _ultimoAvisoTicks;

        private readonly IConnectionMultiplexer _redis;
        private readonly RedisKey _chave;
        private readonly FixedWindowRateLimiterOptions _opcoes;
        private readonly ILogger _logger;
        private readonly Lazy<FixedWindowRateLimiter> _reserva;
        private long _ultimoUso = Stopwatch.GetTimestamp();

        public LimitadorJanelaFixaRedis(
            IConnectionMultiplexer redis, string chave, FixedWindowRateLimiterOptions opcoes, ILogger logger)
        {
            _redis = redis;
            _chave = chave;
            _opcoes = opcoes;
            _logger = logger;
            _reserva = new Lazy<FixedWindowRateLimiter>(() => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = opcoes.PermitLimit,
                Window = opcoes.Window,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
        }

        public override TimeSpan? IdleDuration =>
            // Enquanto o limitador de reserva tem permissões em uso, não pode ser descartado.
            _reserva.IsValueCreated && _reserva.Value.IdleDuration is null
                ? null
                : Stopwatch.GetElapsedTime(Interlocked.Read(ref _ultimoUso));

        public override RateLimiterStatistics? GetStatistics() => null;

        // O middleware tenta primeiro o caminho síncrono; aqui ele não serve (precisa de
        // rede). "Não obtido" faz o middleware seguir para AcquireAsync.
        protected override RateLimitLease AttemptAcquireCore(int permitCount) => Lease.NaoObtido;

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _ultimoUso, Stopwatch.GetTimestamp());

            try
            {
                var usados = (long)await _redis.GetDatabase().ScriptEvaluateAsync(
                    Script,
                    new[] { _chave },
                    new RedisValue[] { (long)_opcoes.Window.TotalMilliseconds, permitCount });

                return usados <= _opcoes.PermitLimit ? Lease.Obtido : Lease.NaoObtido;
            }
            catch (Exception ex)
            {
                // QUALQUER falha no caminho do Redis degrada, nunca vira 500 na rota.
                // Não é só RedisException/RedisTimeoutException: com BacklogPolicy.FailFast
                // o cliente CANCELA (TaskCanceledException) os comandos pendentes quando uma
                // conexão oscila — visto na validação real com 2 réplicas. O token da
                // requisição não é repassado ao Redis, então esse cancelamento nunca é do
                // cliente HTTP.
                AvisarDegradacao(ex);
                return _reserva.Value.AttemptAcquire(permitCount);
            }
        }

        private void AvisarDegradacao(Exception ex)
        {
            var agora = Stopwatch.GetTimestamp();
            var ultimo = Interlocked.Read(ref _ultimoAvisoTicks);
            if (Stopwatch.GetElapsedTime(ultimo, agora) < TimeSpan.FromSeconds(30) && ultimo != 0)
                return;
            if (Interlocked.CompareExchange(ref _ultimoAvisoTicks, agora, ultimo) == ultimo)
                _logger.LogWarning(ex,
                    "Redis indisponível para rate limiting; usando limite em memória por réplica até ele voltar.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _reserva.IsValueCreated)
                _reserva.Value.Dispose();
            base.Dispose(disposing);
        }

        private sealed class Lease : RateLimitLease
        {
            public static readonly Lease Obtido = new(true);
            public static readonly Lease NaoObtido = new(false);

            private Lease(bool obtido) => IsAcquired = obtido;

            public override bool IsAcquired { get; }
            public override IEnumerable<string> MetadataNames => Array.Empty<string>();

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = null;
                return false;
            }
        }
    }
}
