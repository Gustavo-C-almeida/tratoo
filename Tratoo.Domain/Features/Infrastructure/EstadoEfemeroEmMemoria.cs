using Microsoft.Extensions.Caching.Memory;

namespace Tratoo.Domain.Features.Infrastructure
{
    /// <summary>
    /// <see cref="IEstadoEfemero"/> no IMemoryCache do processo — o mesmo armazenamento e a
    /// mesma expiração absoluta que os serviços usavam antes. Correto para UMA réplica;
    /// com mais de uma, cada processo enxerga só o próprio estado (use o Redis).
    ///
    /// Diferença deliberada em relação ao código antigo: incrementar e "definir se
    /// ausente" agora são atômicos (antes era ler → somar → gravar, que perdia contagem
    /// sob requisições simultâneas).
    /// </summary>
    public sealed class EstadoEfemeroEmMemoria : IEstadoEfemero
    {
        private readonly IMemoryCache _cache;
        private readonly object _trava = new();

        public EstadoEfemeroEmMemoria(IMemoryCache cache) => _cache = cache;

        public Task DefinirAsync<T>(string chave, T valor, TimeSpan ttl)
        {
            _cache.Set(chave, valor, ttl);
            return Task.CompletedTask;
        }

        public Task<bool> DefinirSeAusenteAsync<T>(string chave, T valor, TimeSpan ttl)
        {
            lock (_trava)
            {
                if (_cache.TryGetValue(chave, out _))
                    return Task.FromResult(false);

                _cache.Set(chave, valor, ttl);
                return Task.FromResult(true);
            }
        }

        public Task<T?> ObterAsync<T>(string chave) =>
            Task.FromResult(_cache.TryGetValue(chave, out T? valor) ? valor : default);

        public Task RemoverAsync(params string[] chaves)
        {
            foreach (var chave in chaves)
                _cache.Remove(chave);
            return Task.CompletedTask;
        }

        public Task<long> IncrementarAsync(string chave, TimeSpan ttl)
        {
            lock (_trava)
            {
                var atual = _cache.TryGetValue(chave, out long valor) ? valor : 0;
                _cache.Set(chave, ++atual, ttl);
                return Task.FromResult(atual);
            }
        }
    }
}
