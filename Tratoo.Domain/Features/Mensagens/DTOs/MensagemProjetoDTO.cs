using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public class MensagemProjetoDTO
    {
        public Guid Id { get; set; }
        public int ProjetoId { get; set; }
        public int? PrestadorId { get; set; }
        public int RemetenteId { get; set; }
        public string RemetenteNome { get; set; } = string.Empty;
        public string? RemetenteFotoUrl { get; set; }
        public string Texto { get; set; } = string.Empty;
        public MensagemProjetoTipo Tipo { get; set; }
        public Guid? PropostaVersaoId { get; set; }
        // EnviadoEm sempre em UTC — o frontend converte para o timezone local via Date()
        public DateTime EnviadoEm { get; set; }
    }

    public class EnviarMensagemProjetoDTO
    {
        public int ProjetoId { get; set; }
        public int PrestadorId { get; set; }
        public int RemetenteId { get; set; }
        public string Texto { get; set; } = string.Empty;
        public MensagemProjetoTipo Tipo { get; set; } = MensagemProjetoTipo.Texto;
        public Guid? PropostaVersaoId { get; set; }
    }

    /// <summary>Resumo de um chat para a listagem centralizada.</summary>
    public class ChatResumoDTO
    {
        public int ProjetoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        public int PrestadorId { get; set; }
        public string PrestadorNome { get; set; } = string.Empty;

        /// <summary>Nome do outro participante da conversa, do ponto de vista do usuário logado
        /// (contratante vê o prestador; prestador vê o contratante).</summary>
        public string OutroParticipanteNome { get; set; } = string.Empty;

        public string UltimaMensagem { get; set; } = string.Empty;
        public DateTime UltimaMensagemEm { get; set; }
        public int UltimaMensagemPorId { get; set; }
        public int TotalMensagens { get; set; }
    }
}
