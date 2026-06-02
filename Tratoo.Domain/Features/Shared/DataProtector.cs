using System.Security.Cryptography;
using System.Text;

namespace Tratoo.Domain.Features.Shared
{
    /// <summary>
    /// Criptografia AES-256 usada para armazenar CPF/CNPJ em repouso (LGPD Art. 46).
    /// A chave DEVE ser lida de variável de ambiente ou secret manager em produção.
    /// NUNCA commite a chave real no código-fonte.
    /// </summary>
    public static class DataProtector
    {
        // PLACEHOLDER: em produção, ler de variável de ambiente ou secret manager.
        // Normaliza para 32 bytes (AES-256): trunca ou completa com zeros.
        private static readonly byte[] Key = BuildKey("CHAVE_SECRETA_32_BYTES_PLACEHOLDER");

        private static byte[] BuildKey(string seed)
        {
            var raw = Encoding.UTF8.GetBytes(seed);
            var key = new byte[32];
            Buffer.BlockCopy(raw, 0, key, 0, Math.Min(raw.Length, 32));
            return key;
        }

        public static string Encrypt(string data)
        {
            using var aes = Aes.Create();
            aes.Key = Key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();

            byte[] input = Encoding.UTF8.GetBytes(data);
            byte[] encrypted = encryptor.TransformFinalBlock(input, 0, input.Length);

            return Convert.ToBase64String(aes.IV) + "|" +
                   Convert.ToBase64String(encrypted);
        }

        public static string Decrypt(string data)
        {
            var parts = data.Split('|');

            using var aes = Aes.Create();
            aes.Key = Key;
            aes.IV = Convert.FromBase64String(parts[0]);

            using var decryptor = aes.CreateDecryptor();

            byte[] cipher = Convert.FromBase64String(parts[1]);
            byte[] decrypted = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

            return Encoding.UTF8.GetString(decrypted);
        }
    }
}
