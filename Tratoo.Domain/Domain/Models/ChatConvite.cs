namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Espaço de negociação contextualizado criado automaticamente quando um prestador
    /// aceita um convite. Vincula o projeto, o contratante, o prestador e o convite
    /// para que toda a negociação fique rastreável.
    /// </summary>
    public class ChatConvite
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public int ProjetoId { get; set; }
        public Projeto Projeto { get; set; } = null!;

        public int ContratanteId { get; set; }
        public Contratante Contratante { get; set; } = null!;

        public int PrestadorId { get; set; }
        public Prestador.Prestador Prestador { get; set; } = null!;

        /// <summary>Convite que originou este chat (1:1).</summary>
        public Guid ConviteId { get; set; }
        public ConviteProjeto Convite { get; set; } = null!;

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

        public ICollection<ChatConviteMensagem> Mensagens { get; set; } = new List<ChatConviteMensagem>();
    }

    /// <summary>
    /// Mensagem dentro de um ChatConvite. Imutável após persistida.
    /// </summary>
    public class ChatConviteMensagem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatId { get; set; }
        public ChatConvite Chat { get; set; } = null!;

        public int RemetenteId { get; set; }
        public Usuario Remetente { get; set; } = null!;

        /// <summary>Conteúdo da mensagem. Máximo 2000 caracteres.</summary>
        public string Texto { get; set; } = string.Empty;

        public DateTime EnviadoEm { get; set; } = DateTime.UtcNow;
    }
}
