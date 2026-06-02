using System;
using BCrypt.Net;

namespace Tratoo.Domain.Features.Shared
{
    internal static class PasswordHasher
    {
        // Custo (Work Factor): define a "lentidão" do hash. 10~12 é seguro e performático.
        private const int WorkFactor = 12;

        // Hash pré-computado de um valor fictício, usado quando o e-mail não existe.
        // Garante que BCrypt.Verify seja sempre executado, normalizando o tempo de resposta
        // e impedindo timing attacks que revelam se um e-mail está cadastrado.
        internal static readonly string DummyHash =
            BCrypt.Net.BCrypt.HashPassword("__TRATOO_DUMMY_TIMING__", WorkFactor);

        /// Gera o hash da senha usando BCrypt (inclui salt automaticamente).
        public static string Hash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("A senha não pode ser nula ou vazia.", nameof(password));

            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        /// Verifica se a senha informada corresponde ao hash armazenado.
        public static bool Verify(string password, string hashedPassword)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hashedPassword))
                return false;

            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
    }
}
