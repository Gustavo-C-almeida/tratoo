namespace Tratoo.Domain.Features.Propostas
{
    public class PropostaVersaoDTO
    {
        public Guid Id { get; set; }
        public Guid PropostaId { get; set; }
        public int Versao { get; set; }
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
        public int CriadoPor { get; set; }
        public string CriadoPorNome { get; set; } = string.Empty;
        public DateTime CriadoEm { get; set; }
    }
}
