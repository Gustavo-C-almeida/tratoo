using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Auth
{
    public static class LoginValidator
    {
        public static void Validar(string email, string senha)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new NegocioException("E-mail obrigatório.");

            if (string.IsNullOrWhiteSpace(senha))
                throw new NegocioException("Senha obrigatória.");
        }

        /// <summary>
        /// Valida a nova senha durante o fluxo de redefinição.
        /// Mesmas regras do cadastro: mín. 8 chars, 1 maiúscula, 1 dígito, 1 especial.
        /// </summary>
        public static void ValidarNovaSenha(string novaSenha, string confirmarSenha)
        {
            if (string.IsNullOrWhiteSpace(novaSenha))
                throw new NegocioException("Nova senha obrigatória.");

            if (novaSenha.Length > 128)
                throw new NegocioException("Senha muito longa (máximo 128 caracteres).");

            if (!SenhaValida(novaSenha))
                throw new NegocioException("Senha fraca: mínimo 8 caracteres, 1 maiúscula, 1 número e 1 caractere especial.");

            if (novaSenha != confirmarSenha)
                throw new NegocioException("As senhas não conferem.");
        }

        private static bool SenhaValida(string senha)
            => senha.Length >= 8
            && senha.Any(char.IsDigit)
            && senha.Any(char.IsUpper)
            && senha.Any(c => !char.IsLetterOrDigit(c));
    }
}
