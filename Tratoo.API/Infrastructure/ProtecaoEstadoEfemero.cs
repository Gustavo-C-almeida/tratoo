using System.Security.Cryptography;
using System.Text;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Protege o que o Tratoo grava no Redis. Na memória do processo os OTPs ficavam como
    /// SHA-256 simples, e isso bastava; num serviço de rede, não:
    ///  • um código de 6 dígitos tem 900 mil valores — quem ler o Redis reverte o hash em
    ///    milissegundos;
    ///  • o cadastro pendente leva nome, e-mail, IP e o hash BCrypt da senha por até 24 h;
    ///  • as próprias CHAVES carregam e-mail e IP (ex.: "mfa:login:fulano@x.com").
    ///
    /// Então: valores cifrados com AES-256-GCM (confidencialidade + integridade; a chave do
    /// Redis entra como dado associado, de modo que um valor copiado para outra chave não
    /// decifra) e nomes de chave trocados por HMAC-SHA256 — não dá para listar e-mails nem
    /// testar se um e-mail tem código pendente. Só a categoria fica legível
    /// ("tratoo:mfa:…"), para operação e diagnóstico.
    ///
    /// As duas chaves são derivadas (HKDF) de um único segredo, <c>Redis:ChaveProtecao</c>
    /// (32+ bytes em base64), igual em todas as réplicas. Trocar o segredo invalida o que
    /// estava gravado — cada valor ilegível é tratado como ausente (o usuário pede outro
    /// código), nunca como erro.
    /// </summary>
    public sealed class ProtecaoEstadoEfemero
    {
        public const string ChaveConfiguracao = "Redis:ChaveProtecao";

        private const byte Versao = 1;
        private const int TamanhoNonce = 12;
        private const int TamanhoTag = 16;
        private const int TamanhoCabecalho = 1 + TamanhoNonce + TamanhoTag;

        private readonly byte[] _chaveCifra;
        private readonly byte[] _chaveNomes;

        public ProtecaoEstadoEfemero(byte[] segredo)
        {
            if (segredo.Length < 32)
                throw new ArgumentException("O segredo de proteção precisa ter ao menos 32 bytes.", nameof(segredo));

            _chaveCifra = HKDF.DeriveKey(HashAlgorithmName.SHA256, segredo, 32,
                info: Encoding.UTF8.GetBytes("tratoo/estado-efemero/cifra/v1"));
            _chaveNomes = HKDF.DeriveKey(HashAlgorithmName.SHA256, segredo, 32,
                info: Encoding.UTF8.GetBytes("tratoo/estado-efemero/nomes/v1"));
        }

        /// <summary>Lê <c>Redis:ChaveProtecao</c>; falha alto (na subida) se ausente ou curta.</summary>
        public static ProtecaoEstadoEfemero DeConfiguracao(IConfiguration configuration)
        {
            var base64 = configuration[ChaveConfiguracao];
            if (string.IsNullOrWhiteSpace(base64))
                throw new InvalidOperationException(
                    $"Redis configurado sem {ChaveConfiguracao}. Gere com `openssl rand -base64 32` e use o MESMO valor em todas as réplicas.");

            byte[] segredo;
            try { segredo = Convert.FromBase64String(base64.Trim()); }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"{ChaveConfiguracao} não é base64 válido.", ex);
            }

            if (segredo.Length < 32)
                throw new InvalidOperationException($"{ChaveConfiguracao} precisa ter ao menos 32 bytes (tem {segredo.Length}).");

            return new ProtecaoEstadoEfemero(segredo);
        }

        /// <summary>
        /// "mfa:login:fulano@x.com" → "tratoo:mfa:&lt;HMAC em base64url&gt;". Determinístico:
        /// todas as réplicas chegam ao mesmo nome.
        /// </summary>
        public string NomeDaChave(string chaveLogica)
        {
            var categoria = chaveLogica.Split(':', 2)[0];
            if (categoria.Length == 0 || categoria.Length > 40 || !categoria.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                categoria = "x";

            var mac = HMACSHA256.HashData(_chaveNomes, Encoding.UTF8.GetBytes(chaveLogica));
            return $"tratoo:{categoria}:{Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
        }

        /// <summary>[versão][nonce 12][tag 16][cifrado]. O nome da chave é dado associado.</summary>
        public byte[] Cifrar(ReadOnlySpan<byte> claro, string nomeDaChave)
        {
            var saida = new byte[TamanhoCabecalho + claro.Length];
            saida[0] = Versao;
            var nonce = saida.AsSpan(1, TamanhoNonce);
            var tag = saida.AsSpan(1 + TamanhoNonce, TamanhoTag);
            var cifrado = saida.AsSpan(TamanhoCabecalho);

            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(_chaveCifra, TamanhoTag);
            aes.Encrypt(nonce, claro, cifrado, tag, Encoding.UTF8.GetBytes(nomeDaChave));
            return saida;
        }

        /// <summary>null se o valor não decifra (segredo trocado, adulterado, ou de outra chave).</summary>
        public byte[]? Decifrar(byte[] dados, string nomeDaChave)
        {
            if (dados.Length < TamanhoCabecalho || dados[0] != Versao)
                return null;

            var claro = new byte[dados.Length - TamanhoCabecalho];
            try
            {
                using var aes = new AesGcm(_chaveCifra, TamanhoTag);
                aes.Decrypt(
                    dados.AsSpan(1, TamanhoNonce),
                    dados.AsSpan(TamanhoCabecalho),
                    dados.AsSpan(1 + TamanhoNonce, TamanhoTag),
                    claro,
                    Encoding.UTF8.GetBytes(nomeDaChave));
                return claro;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }
}
