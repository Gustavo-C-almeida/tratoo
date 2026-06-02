using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Tratoo.Domain.Features.Shared
{
    public static class ValidadorCNPJ
    {
        public static bool Validar(string cnpj)
        {
            if (string.IsNullOrWhiteSpace(cnpj))
                return false;

            // Remove tudo que não é número
            cnpj = Regex.Replace(cnpj, @"\D", "");

            // Deve ter 14 dígitos
            if (cnpj.Length != 14)
                return false;

            // Bloqueia CNPJs inválidos conhecidos (todos iguais)
            if (cnpj.Distinct().Count() == 1)
                return false;

            int[] multiplicador1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int[] multiplicador2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

            // Primeiro dígito verificador
            string tempCnpj = cnpj.Substring(0, 12);
            int soma = 0;

            for (int i = 0; i < 12; i++)
                soma += (tempCnpj[i] - '0') * multiplicador1[i];

            int resto = soma % 11;
            int dig1 = resto < 2 ? 0 : 11 - resto;

            // Segundo dígito verificador
            tempCnpj += dig1;
            soma = 0;

            for (int i = 0; i < 13; i++)
                soma += (tempCnpj[i] - '0') * multiplicador2[i];

            resto = soma % 11;
            int dig2 = resto < 2 ? 0 : 11 - resto;

            // Validação final
            return cnpj.EndsWith($"{dig1}{dig2}");
        }
    }
}
