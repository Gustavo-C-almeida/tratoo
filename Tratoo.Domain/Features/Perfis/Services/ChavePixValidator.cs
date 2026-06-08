using System.Text.RegularExpressions;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;

namespace Tratoo.Domain.Features.Perfis
{
    /// <summary>
    /// Valida, normaliza e mascara chaves PIX conforme o tipo (CPF, CNPJ, e-mail,
    /// telefone ou chave aleatória/EVP). A normalização garante o formato esperado
    /// pelo gateway (Asaas) antes do armazenamento criptografado.
    /// </summary>
    public static class ChavePixValidator
    {
        private static readonly Regex EmailRegex =
            new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        private static readonly Regex EvpRegex =
            new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
                RegexOptions.Compiled);

        /// <summary>
        /// Valida e retorna a chave normalizada. Lança <see cref="NegocioException"/> se inválida.
        /// </summary>
        public static string ValidarENormalizar(TipoPix tipo, string chave)
        {
            if (string.IsNullOrWhiteSpace(chave))
                throw new NegocioException("Informe a chave PIX.");

            chave = chave.Trim();

            return tipo switch
            {
                TipoPix.CPF => ValidarCpf(chave),
                TipoPix.CNPJ => ValidarCnpj(chave),
                TipoPix.Email => ValidarEmail(chave),
                TipoPix.Telefone => ValidarTelefone(chave),
                TipoPix.Aleatoria => ValidarEvp(chave),
                _ => throw new NegocioException("Tipo de chave PIX inválido.")
            };
        }

        private static string ValidarCpf(string chave)
        {
            var digits = SomenteDigitos(chave);
            if (digits.Length != 11 || !CpfValido(digits))
                throw new NegocioException("Chave PIX do tipo CPF inválida.");
            return digits;
        }

        private static string ValidarCnpj(string chave)
        {
            var digits = SomenteDigitos(chave);
            if (digits.Length != 14 || !CnpjValido(digits))
                throw new NegocioException("Chave PIX do tipo CNPJ inválida.");
            return digits;
        }

        private static string ValidarEmail(string chave)
        {
            var email = chave.ToLowerInvariant();
            if (email.Length > 254 || !EmailRegex.IsMatch(email))
                throw new NegocioException("Chave PIX do tipo e-mail inválida.");
            return email;
        }

        private static string ValidarTelefone(string chave)
        {
            var digits = SomenteDigitos(chave);

            // Aceita com ou sem código do país (55). Espera DDD (2) + número (8 ou 9).
            if (digits.StartsWith("55") && digits.Length is 12 or 13)
                digits = digits[2..];

            if (digits.Length is not (10 or 11))
                throw new NegocioException("Chave PIX do tipo telefone inválida. Use DDD + número.");

            // Formato E.164 exigido pelo PIX: +55DDDNUMERO
            return "+55" + digits;
        }

        private static string ValidarEvp(string chave)
        {
            if (!EvpRegex.IsMatch(chave))
                throw new NegocioException("Chave PIX aleatória inválida (formato UUID esperado).");
            return chave.ToLowerInvariant();
        }

        // ── Máscara para exibição (nunca retorna o valor completo) ──────────────────
        public static string Mascarar(TipoPix tipo, string chaveNormalizada)
        {
            if (string.IsNullOrEmpty(chaveNormalizada)) return string.Empty;

            return tipo switch
            {
                TipoPix.Email => MascararEmail(chaveNormalizada),
                TipoPix.Aleatoria => MascararExtremos(chaveNormalizada, 4, 4),
                _ => MascararDigitos(chaveNormalizada) // CPF, CNPJ, Telefone
            };
        }

        private static string MascararDigitos(string valor)
        {
            // Mantém apenas os 2 últimos dígitos visíveis.
            var digits = SomenteDigitos(valor);
            if (digits.Length <= 2) return new string('•', digits.Length);
            return new string('•', digits.Length - 2) + digits[^2..];
        }

        private static string MascararEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 0) return "•••";
            var user = email[..at];
            var dominio = email[(at + 1)..];
            var userMasc = user.Length <= 1 ? "•" : user[0] + new string('•', Math.Min(user.Length - 1, 5));
            var domMasc = dominio.Length <= 1 ? "•" : dominio[0] + new string('•', Math.Min(dominio.Length - 1, 4));
            return $"{userMasc}@{domMasc}";
        }

        private static string MascararExtremos(string valor, int inicio, int fim)
        {
            if (valor.Length <= inicio + fim) return new string('•', valor.Length);
            return valor[..inicio] + "…" + valor[^fim..];
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────
        public static string SomenteDigitos(string s)
            => new(s.Where(char.IsDigit).ToArray());

        private static bool CpfValido(string cpf)
        {
            if (cpf.Distinct().Count() == 1) return false; // todos iguais

            int Soma(int len)
            {
                int soma = 0, peso = len + 1;
                for (int i = 0; i < len; i++) soma += (cpf[i] - '0') * peso--;
                return soma;
            }
            int Dv(int soma) { int r = soma % 11; return r < 2 ? 0 : 11 - r; }

            return Dv(Soma(9)) == cpf[9] - '0' && Dv(Soma(10)) == cpf[10] - '0';
        }

        private static bool CnpjValido(string cnpj)
        {
            if (cnpj.Distinct().Count() == 1) return false;

            int[] peso1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int[] peso2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

            int Calc(int[] pesos, int len)
            {
                int soma = 0;
                for (int i = 0; i < len; i++) soma += (cnpj[i] - '0') * pesos[i];
                int r = soma % 11;
                return r < 2 ? 0 : 11 - r;
            }

            return Calc(peso1, 12) == cnpj[12] - '0' && Calc(peso2, 13) == cnpj[13] - '0';
        }
    }
}
