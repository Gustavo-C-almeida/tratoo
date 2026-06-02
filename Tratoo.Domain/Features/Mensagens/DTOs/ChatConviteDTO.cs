namespace Tratoo.Domain.Features.Mensagens
{
    public class ChatConviteDTO
    {
        public Guid Id { get; set; }
        public Guid ConviteId { get; set; }
        public int ProjetoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        public int ContratanteId { get; set; }
        public string ContratanteNome { get; set; } = string.Empty;
        public int PrestadorId { get; set; }
        public string PrestadorNome { get; set; } = string.Empty;
        public DateTime CriadoEm { get; set; }
        public int TotalMensagens { get; set; }
    }

    public class ChatConviteMensagemDTO
    {
        public Guid Id { get; set; }
        public Guid ChatId { get; set; }
        public int RemetenteId { get; set; }
        public string RemetenteNome { get; set; } = string.Empty;
        public string Texto { get; set; } = string.Empty;
        public DateTime EnviadoEm { get; set; }
    }

    public class EnviarChatMensagemDTO
    {
        public Guid ChatId { get; set; }
        public int RemetenteId { get; set; }
        public string Texto { get; set; } = string.Empty;
    }
}
