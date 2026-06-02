using System.Text.RegularExpressions;

namespace Tratoo.Domain.Features.Auth
{
    public static class CadastroValidator
    {
        public static void Validar(CadastroUserDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Nome) || dto.Nome.Length < 3)
                throw new ArgumentException("Nome inválido");
            if (dto.Nome.Length > 100)
                throw new ArgumentException("Nome muito longo (máximo 100 caracteres)");

            if (!EmailValido(dto.Email))
                throw new ArgumentException("E-mail inválido");
            if (dto.Email.Length > 254)
                throw new ArgumentException("E-mail muito longo");

            if (dto.Senha.Length > 128)
                throw new ArgumentException("Senha muito longa (máximo 128 caracteres)");
            if (!SenhaValida(dto.Senha))
                throw new ArgumentException("Senha fraca: mínimo 8 caracteres, 1 número, 1 maiúscula, 1 caractere especial");

            if (dto.Senha != dto.ConfirmarSenha)
                throw new ArgumentException("As senhas não conferem");

            var tipoNorm = dto.Tipo?.ToLower();
            if (tipoNorm != "prestador" && tipoNorm != "contratante")
                throw new ArgumentException("Tipo de usuário inválido. Use 'prestador' ou 'contratante'");
        }

        private static bool EmailValido(string email)
            => Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        private static bool SenhaValida(string senha)
            => senha.Length >= 8
            && senha.Any(char.IsDigit)          // pelo menos 1 número
            && senha.Any(char.IsUpper)          // pelo menos 1 maiúscula
            && senha.Any(c => !char.IsLetterOrDigit(c)); // pelo menos 1 caractere especial
    }
}
