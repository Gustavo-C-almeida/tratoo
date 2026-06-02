using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tratoo.Domain.Models.Prestador;
using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models.Financeiro
{
    public class ContaBancaria
    {
        public int Id { get; set; }

        // Relacionamento
        public int PrestadorId { get; set; }
        public Tratoo.Domain.Models.Prestador.Prestador Prestador { get; set; }

        // Dados bancários
        public string Banco { get; set; }
        public string Agencia { get; set; }

        // Criptografado
        public string ContaCriptografada { get; set; }

        // PIX
        public string PixChave { get; set; }
        public TipoPix TipoPix { get; set; }

        // Status
        public bool Ativa { get; set; } = true;

        public DateTime CriadoEm { get; set; } = DateTime.Now;
        public DateTime? AtualizadoEm { get; set; }
    }
}
