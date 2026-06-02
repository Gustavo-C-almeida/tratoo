using Microsoft.Extensions.Caching.Memory;

namespace Tratoo.Domain.Features.Infrastructure
{
    public class CacheTempService : ICacheTempService
    {
        private readonly IMemoryCache _cache;

        public CacheTempService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public void Salvar<T>(string chave, T dados, TimeSpan expiracao)
        {
            _cache.Set(chave, dados, expiracao);
        }

        public T? Obter<T>(string chave)
        {
            _cache.TryGetValue(chave, out T? dados);
            return dados;
        }

        public void Remover(string chave)
        {
            _cache.Remove(chave);
        }
    }
}
