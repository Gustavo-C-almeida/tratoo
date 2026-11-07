using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Chaves HMAC-SHA256 do JWT de acesso.
    ///
    ///  • <c>Jwt:SecretKey</c> (variável <c>Jwt__SecretKey</c>) — ASSINA e valida;
    ///  • <c>Jwt:SecretKeyAnterior</c> (opcional) — só VALIDA, durante uma troca de chave.
    ///
    /// Cada token leva no cabeçalho o <c>kid</c> da chave que o assinou (um HMAC da própria
    /// chave, truncado — não revela a chave), e a validação escolhe a chave por ele. Token
    /// sem <c>kid</c> (emitido antes desta versão) é conferido contra as chaves configuradas.
    ///
    /// Trocar a chave não desloga ninguém: o access vale no máximo 15 min e o refresh (opaco,
    /// no Redis) não depende dela. A anterior só evita a rajada de 401 + refresh na troca e
    /// o vai e volta entre réplicas durante o deploy; pode sair depois de 15+ min
    /// (Docs/ROTACAO-CHAVES.md).
    /// </summary>
    public sealed class ChavesJwt
    {
        public const string ChaveConfiguracao = "Jwt:SecretKey";
        public const string VariavelDeAmbiente = "Jwt__SecretKey";
        public const string ChaveAnteriorConfiguracao = "Jwt:SecretKeyAnterior";
        public const string VariavelDeAmbienteAnterior = "Jwt__SecretKeyAnterior";

        /// <summary>HS256 exige 256 bits; a biblioteca já recusa assinar com menos.</summary>
        public const int TamanhoMinimoBytes = 32;

        private const string PrefixoPlaceholder = "DEFINA_";
        private static readonly byte[] InfoKid = Encoding.UTF8.GetBytes("tratoo/jwt/kid");

        public SymmetricSecurityKey Atual { get; }
        public SymmetricSecurityKey? Anterior { get; }

        /// <summary>Chaves aceitas na validação: a atual e, numa troca, a anterior.</summary>
        public IReadOnlyList<SecurityKey> Validacao { get; }

        private ChavesJwt(SymmetricSecurityKey atual, SymmetricSecurityKey? anterior)
        {
            Atual = atual;
            Anterior = anterior;
            Validacao = anterior is null ? new SecurityKey[] { atual } : new SecurityKey[] { atual, anterior };
        }

        public static ChavesJwt DeConfiguracao(IConfiguration configuration) =>
            Preparar(configuration[ChaveConfiguracao], configuration[ChaveAnteriorConfiguracao]);

        /// <summary>
        /// Valida e monta as chaves. Lança <see cref="InvalidOperationException"/> (mensagem
        /// sem o valor) se a atual faltar, for placeholder ou curta, ou se a anterior for curta
        /// ou igual à atual.
        /// </summary>
        public static ChavesJwt Preparar(string? atual, string? anterior)
        {
            var chaveAtual = Criar(atual, VariavelDeAmbiente);
            if (string.IsNullOrEmpty(anterior))
                return new ChavesJwt(chaveAtual, null);

            var chaveAnterior = Criar(anterior, VariavelDeAmbienteAnterior);
            if (CryptographicOperations.FixedTimeEquals(chaveAtual.Key, chaveAnterior.Key))
                throw new InvalidOperationException(
                    $"{VariavelDeAmbienteAnterior} é igual a {VariavelDeAmbiente}. Numa troca, {VariavelDeAmbiente} é a " +
                    $"chave NOVA e {VariavelDeAmbienteAnterior} a antiga; fora de uma troca, remova {VariavelDeAmbienteAnterior}.");

            return new ChavesJwt(chaveAtual, chaveAnterior);
        }

        /// <summary>Identificador público da chave (16 hex). Não revela a chave.</summary>
        public static string Kid(byte[] chave) =>
            Convert.ToHexString(HMACSHA256.HashData(chave, InfoKid)[..8]).ToLowerInvariant();

        private static SymmetricSecurityKey Criar(string? texto, string variavel)
        {
            if (string.IsNullOrWhiteSpace(texto))
                throw new InvalidOperationException($"Chave do JWT ausente: defina {variavel}.");
            if (texto.StartsWith(PrefixoPlaceholder, StringComparison.Ordinal))
                throw new InvalidOperationException($"{variavel} ainda está com o placeholder do arquivo de exemplo.");

            var bytes = Encoding.UTF8.GetBytes(texto);
            if (bytes.Length < TamanhoMinimoBytes)
                throw new InvalidOperationException(
                    $"{variavel} precisa ter ao menos {TamanhoMinimoBytes} bytes. Gere com `openssl rand -base64 48`.");

            return new SymmetricSecurityKey(bytes) { KeyId = Kid(bytes) };
        }
    }
}
