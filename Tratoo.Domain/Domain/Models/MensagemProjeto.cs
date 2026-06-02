namespace Tratoo.Domain.Models
{
    public enum MensagemProjetoTipo
    {
        Texto,
        Contraproposta,
        Sistema
    }

    /// <summary>
    /// Chat vinculado ao projeto — isolado por par (contratante + prestador).
    /// Cada prestador com proposta ativa tem sua conversa privada com o contratante.
    /// </summary>
    public class MensagemProjeto
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public int ProjetoId { get; set; }
        public Projeto Projeto { get; set; } = null!;

        public int RemetenteId { get; set; }
        public Usuario Remetente { get; set; } = null!;

        /// <summary>
        /// ID do prestador que participa desta conversa.
        /// Garante isolamento: cada prestador enxerga apenas suas mensagens com o contratante.
        /// Nullable para compatibilidade com mensagens históricas.
        /// </summary>
        public int? PrestadorId { get; set; }

        /// <summary>Conteúdo da mensagem. Máximo 2000 caracteres.</summary>
        public string Texto { get; set; } = string.Empty;

        public MensagemProjetoTipo Tipo { get; set; } = MensagemProjetoTipo.Texto;

        /// <summary>Vínculo opcional com a versão da proposta relacionada (quando Tipo = Contraproposta).</summary>
        public Guid? PropostaVersaoId { get; set; }
        public PropostaVersao? PropostaVersao { get; set; }

        public DateTime EnviadoEm { get; set; } = DateTime.UtcNow;
    }
}
