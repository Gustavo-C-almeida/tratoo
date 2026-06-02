using System.Security.Cryptography;
using System.Text;

namespace Tratoo.Domain.Features.Shared
{
    // SHA-256 com comparação em tempo constante para códigos OTP de curta duração.
    // BCrypt seria excessivo para OTPs de 6 dígitos com TTL de 5 minutos armazenados
    // em memória — a lentidão intencional do BCrypt não traz benefício aqui e prejudica
    // a performance (~300 ms desnecessários por validação MFA).
    internal static class SecureHasher
    {
        public static string Hash(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToBase64String(bytes);
        }

        public static bool Verify(string value, string storedHash)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(storedHash))
                return false;

            var computedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));

            byte[] storedBytes;
            try { storedBytes = Convert.FromBase64String(storedHash); }
            catch { return false; }

            // Comparação em tempo constante para evitar timing attacks na verificação do hash.
            return CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes);
        }
    }
}
