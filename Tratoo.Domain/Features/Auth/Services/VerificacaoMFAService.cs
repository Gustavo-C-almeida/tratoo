using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Auth
{
    public class VerificacaoMFAService : IVerificacaoMFAService
    {
        private readonly IMemoryCache _cache;

        public VerificacaoMFAService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public string GerarECriar(string email, string tipo)
        {
            string codigo = GerarCodigo();
            string hash = SecureHasher.Hash(codigo);

            _cache.Set(GetKey(email, tipo), hash, TimeSpan.FromMinutes(5));

            return codigo;
        }

        public void Validar(string email, string codigoDigitado, string tipo)
        {
            var key = GetKey(email, tipo);

            if (!_cache.TryGetValue(key, out string? hash))
                throw new NegocioException("Código expirado ou inválido");

            if (!SecureHasher.Verify(codigoDigitado, hash!))
                throw new NegocioException("Código inválido");

            _cache.Remove(key);
        }

        private static string GerarCodigo()
        {
            return RandomNumberGenerator
                .GetInt32(100000, 1000000)
                .ToString();
        }

        private static string GetKey(string email, string tipo)
        {
            return $"mfa:{tipo}:{email}";
        }
    }
}
