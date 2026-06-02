using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Models
{
    public class Mensagem
    {
        public Guid Id { get; set; }

        public Guid ConversaId { get; set; }
        public Conversa Conversa { get; set; }

        public int RemetenteId { get; set; }
        public Usuario Remetente { get; set; }

        public string Conteudo { get; set; }

        public DateTime EnviadaEm { get; set; } = DateTime.UtcNow;

        public bool Lida { get; set; } = false;
        public TipoMensagem Tipo { get; set; } = TipoMensagem.Texto;

        public enum TipoMensagem
        {
            Texto,
            Sistema,
            Anexo,
            LinkEntrega
        }

    }

}
