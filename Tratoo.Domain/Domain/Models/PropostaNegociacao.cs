using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Models
{
    public class PropostaNegociacao
    {
        public Guid Id { get; set; }

        public Guid PropostaId { get; set; }
        public Proposta Proposta { get; set; }

        public decimal ValorSugerido { get; set; }

        public DateTime PrazoEntregaSugerido { get; set; }

        public string Mensagem { get; set; }

        public TipoAutor Autor { get; set; }

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    }

    public enum TipoAutor
    {
        Contratante,
        Prestador
    }
}
