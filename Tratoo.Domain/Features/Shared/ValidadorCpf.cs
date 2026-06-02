using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Features.Shared
{
    using System.Linq;
    using System.Text.RegularExpressions;

    public static class ValidadorCpf
    {
        public static bool Validar(string cpf)
        {
            if (string.IsNullOrWhiteSpace(cpf))
                return false;

            // Remove tudo que não é número
            cpf = Regex.Replace(cpf, @"\D", "");

            // Deve ter 11 dígitos
            if (cpf.Length != 11)
                return false;

            // Bloqueia CPFs inválidos conhecidos
            if (cpf.Distinct().Count() == 1)
                return false;

            // Calcula primeiro dígito
            int soma = 0;
            for (int i = 0; i < 9; i++)
                soma += (cpf[i] - '0') * (10 - i);

            int dig1 = (soma * 10) % 11;
            if (dig1 == 10) dig1 = 0;

            if (dig1 != (cpf[9] - '0'))
                return false;

            // Calcula segundo dígito
            soma = 0;
            for (int i = 0; i < 10; i++)
                soma += (cpf[i] - '0') * (11 - i);

            int dig2 = (soma * 10) % 11;
            if (dig2 == 10) dig2 = 0;

            return dig2 == (cpf[10] - '0');
        }
    }

}
