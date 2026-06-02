using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Models
{
    public class Conversa
    {
        public Guid Id { get; set; }

        // Participantes fixos
        public int ContratanteId { get; set; }
        public Contratante Contratante { get; set; }

        public int PrestadorId { get; set; }
        public Prestador.Prestador Prestador { get; set; }

        // Contexto da conversa
        public Guid? PropostaId { get; set; }
        public Proposta? Proposta { get; set; }

        public Guid? ContratoId { get; set; }
        public Contrato? Contrato { get; set; }

        // Controle
        public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
        public DateTime? EncerradaEm { get; set; }

        // Navegação
        public ICollection<Mensagem> Mensagens { get; set; } = new List<Mensagem>();
    }
}
