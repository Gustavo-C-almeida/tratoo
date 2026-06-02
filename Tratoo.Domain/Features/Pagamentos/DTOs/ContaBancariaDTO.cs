using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Pagamentos
{
    public class ContaBancariaDTO
    {
        public string Banco { get; set; }
        public string Agencia { get; set; }
        public string Conta { get; set; }

        public string Pix { get; set; }
        public TipoPix TipoPix { get; set; }
    }

}
