namespace Tratoo.Domain.Features.Propostas
{
    /// <summary>Cria nova PropostaVersao — usada pelo prestador e pelo contratante.</summary>
    public class ContrapropostaDTO
    {
        public Guid PropostaId { get; set; }
        public int UsuarioId { get; set; }

        public string Objetivo { get; set; } = string.Empty;
        public string Escopo { get; set; } = string.Empty;
        public string? Exclusoes { get; set; }
        public int RevisoesInclusas { get; set; }
        public DateTime PrazoTotal { get; set; }
        public decimal ValorTotal { get; set; }
        public decimal? Entrada { get; set; }
        public string FormaPagamento { get; set; } = "PIX";
        public string? Observacoes { get; set; }
        public string? MarcosJson { get; set; }
    }
}
