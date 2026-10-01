using System.Security.Cryptography;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Infrastructure;

namespace Tratoo.Domain.Features.Auth
{
    /// <summary>
    /// Códigos de 6 dígitos por e-mail (login MFA, reset de senha, confirmação de cadastro).
    /// O código fica só como hash (SecureHasher), no <see cref="IEstadoEfemero"/> — em
    /// memória com 1 réplica, no Redis com várias (o código gerado numa réplica precisa
    /// valer na outra).
    ///
    /// Máximo de <see cref="MaxTentativas"/> erros por código, como já faziam o OTP de
    /// assinatura e o token de dados bancários. Antes não havia limite: a única proteção era
    /// o rate limit por IP, que um atacante distribuído contorna.
    /// </summary>
    public class VerificacaoMFAService : IVerificacaoMFAService
    {
        public const int MaxTentativas = 5;
        private static readonly TimeSpan Validade = TimeSpan.FromMinutes(5);

        private readonly IEstadoEfemero _estado;

        public VerificacaoMFAService(IEstadoEfemero estado)
        {
            _estado = estado;
        }

        public async Task<string> GerarECriarAsync(string email, string tipo)
        {
            string codigo = GerarCodigo();

            // Código novo zera as tentativas do anterior.
            await _estado.RemoverAsync(ChaveTentativas(email, tipo));
            await _estado.DefinirAsync(Chave(email, tipo), SecureHasher.Hash(codigo), Validade);

            return codigo;
        }

        public async Task ValidarAsync(string email, string codigoDigitado, string tipo)
        {
            var chave = Chave(email, tipo);
            var chaveTentativas = ChaveTentativas(email, tipo);

            var hash = await _estado.ObterAsync<string>(chave);
            if (hash is null)
                throw new NegocioException("Código expirado ou inválido");

            if (!SecureHasher.Verify(codigoDigitado, hash))
            {
                var tentativas = await _estado.IncrementarAsync(chaveTentativas, Validade);
                if (tentativas >= MaxTentativas)
                {
                    // Invalida o código: a partir daqui, só pedindo outro.
                    await _estado.RemoverAsync(chave, chaveTentativas);
                    throw new NegocioException("Muitas tentativas inválidas. Solicite um novo código.");
                }

                throw new NegocioException("Código inválido");
            }

            // Uso único.
            await _estado.RemoverAsync(chave, chaveTentativas);
        }

        private static string GerarCodigo()
        {
            return RandomNumberGenerator
                .GetInt32(100000, 1000000)
                .ToString();
        }

        private static string Chave(string email, string tipo) => $"mfa:{tipo}:{email}";
        private static string ChaveTentativas(string email, string tipo) => $"mfa-tentativas:{tipo}:{email}";
    }
}
