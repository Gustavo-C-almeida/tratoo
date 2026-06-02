using Tratoo.Domain.Models;

namespace Tratoo.API.Requests
{
    public class MensagemProjetoRequest
    {
        public string Texto { get; set; } = string.Empty;
        public MensagemProjetoTipo Tipo { get; set; } = MensagemProjetoTipo.Texto;
        public Guid? PropostaVersaoId { get; set; }
        /// <summary>Obrigatório quando o remetente é o contratante — identifica o par da conversa.</summary>
        public int? PrestadorId { get; set; }
    }
}
