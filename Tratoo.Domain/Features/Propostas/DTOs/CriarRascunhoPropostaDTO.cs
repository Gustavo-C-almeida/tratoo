namespace Tratoo.Domain.Features.Propostas
{
    /// <summary>Usado tanto para criar o rascunho quanto para criar uma contraproposta.</summary>
    public class CriarRascunhoPropostaDTO
    {
        public int ProjetoId { get; set; }
        public int PrestadorId { get; set; }
        public DateTime ValidoAte { get; set; }

        // Conteúdo da versão inicial
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
