using System.Security.Cryptography;
using System.Text;

namespace Tratoo.Domain.Features.Shared
{
    /// <summary>
    /// Criptografia AES-256 dos dados sensíveis em repouso — conta bancária, chave PIX e
    /// CPF/CNPJ (LGPD Art. 46).
    ///
    /// A CHAVE NÃO FICA NO CÓDIGO. Ela vem da configuração <c>DataProtector:Chave</c>
    /// (variável de ambiente <c>DataProtector__Chave</c>; em desenvolvimento, user-secrets ou
    /// appsettings local). A API chama <see cref="Configurar"/> na subida e NÃO sobe sem ela
    /// (Program.cs); fora da API (ferramentas, testes) vale a variável de ambiente.
    ///
    /// Compatibilidade — o que NÃO pode mudar sem migração de dados:
    ///  • derivação da chave (<see cref="DerivarChave"/>): bytes UTF-8 do texto configurado,
    ///    truncados ou completados com zeros até 32 bytes;
    ///  • formato do valor cifrado: <c>base64(IV) + "|" + base64(AES-CBC/PKCS7)</c>.
    /// Com o MESMO texto de chave, este código lê tudo o que a versão anterior gravou.
    /// Trocar a chave exige recifrar os dados existentes (rotação) — as sobrecargas com
    /// chave explícita existem para isso: ler com a antiga, gravar com a nova.
    /// </summary>
    public static class DataProtector
    {
        /// <summary>Chave de configuração (variável de ambiente: <c>DataProtector__Chave</c>).</summary>
        public const string ChaveConfiguracao = "DataProtector:Chave";
        public const string VariavelDeAmbiente = "DataProtector__Chave";

        /// <summary>Prefixo dos placeholders dos arquivos de exemplo — nunca é uma chave válida.</summary>
        private const string PrefixoPlaceholder = "DEFINA_";

        private static volatile byte[]? _chave;

        /// <summary>
        /// Define a chave do processo. Lança se estiver ausente ou ainda for o placeholder do
        /// arquivo de exemplo — a API usa isso para se recusar a subir sem chave.
        /// </summary>
        public static void Configurar(string? chave) => _chave = DerivarChave(chave);

        /// <summary>
        /// Deriva a chave AES-256 do texto configurado, EXATAMENTE como sempre foi feito:
        /// bytes UTF-8, truncados ou completados com zeros até 32 bytes. O texto não é
        /// aparado nem normalizado — um espaço a mais no início muda a chave.
        /// </summary>
        public static byte[] DerivarChave(string? chave)
        {
            if (string.IsNullOrWhiteSpace(chave))
                throw new InvalidOperationException(
                    $"Chave de criptografia de dados sensíveis ausente: defina {VariavelDeAmbiente} " +
                    $"(configuração {ChaveConfiguracao}). Em produção ela precisa ser EXATAMENTE a chave " +
                    "com que os dados existentes foram cifrados — uma chave diferente os torna ilegíveis.");

            if (chave.StartsWith(PrefixoPlaceholder, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{VariavelDeAmbiente} ainda está com o placeholder do arquivo de exemplo. Defina uma chave real.");

            var raw = Encoding.UTF8.GetBytes(chave);
            var key = new byte[32];
            Buffer.BlockCopy(raw, 0, key, 0, Math.Min(raw.Length, 32));
            return key;
        }

        public static string Encrypt(string data) => Encrypt(data, ChaveAtual());

        public static string Decrypt(string data) => Decrypt(data, ChaveAtual());

        public static string Encrypt(string data, byte[] chave)
        {
            using var aes = Aes.Create();
            aes.Key = chave;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();

            byte[] input = Encoding.UTF8.GetBytes(data);
            byte[] encrypted = encryptor.TransformFinalBlock(input, 0, input.Length);

            return Convert.ToBase64String(aes.IV) + "|" +
                   Convert.ToBase64String(encrypted);
        }

        public static string Decrypt(string data, byte[] chave)
        {
            var parts = data.Split('|');

            using var aes = Aes.Create();
            aes.Key = chave;
            aes.IV = Convert.FromBase64String(parts[0]);

            using var decryptor = aes.CreateDecryptor();

            byte[] cipher = Convert.FromBase64String(parts[1]);
            byte[] decrypted = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

            return Encoding.UTF8.GetString(decrypted);
        }

        /// <summary>
        /// true se o valor tem a FORMA de algo cifrado por esta classe (IV de 16 bytes e
        /// blocos de 16), sem tentar decifrar. Serve para separar dado cifrado de lixo
        /// (ex.: valores fictícios de seed) antes de testar a chave.
        /// </summary>
        public static bool PareceCifrado(string? valor)
        {
            if (string.IsNullOrEmpty(valor))
                return false;

            var parts = valor.Split('|');
            if (parts.Length != 2)
                return false;

            Span<byte> iv = stackalloc byte[16];
            if (!Convert.TryFromBase64String(parts[0], iv, out var lidosIv) || lidosIv != 16)
                return false;

            var cifrado = new byte[(parts[1].Length * 3 / 4) + 3];
            return Convert.TryFromBase64String(parts[1], cifrado, out var lidos)
                   && lidos > 0 && lidos % 16 == 0;
        }

        private static byte[] ChaveAtual()
        {
            var chave = _chave;
            if (chave is not null)
                return chave;

            // Fora da API (ferramentas, testes): vale a variável de ambiente. Se também não
            // existir, DerivarChave lança com a instrução de como configurar.
            chave = DerivarChave(Environment.GetEnvironmentVariable(VariavelDeAmbiente));
            _chave = chave;
            return chave;
        }
    }
}
