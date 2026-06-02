using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Exceptions
{
    public class OperacaoCanceladaException : Exception
    {
        public OperacaoCanceladaException()
            : base("Operação cancelada pelo usuário.")
        {
        }
    }
}
