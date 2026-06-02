namespace Tratoo.API.Requests
{
    public class EnviarChatMensagemRequest
    {
        public string Texto { get; set; } = string.Empty;
    }

    /// <summary>
    /// Payload para o contratante criar proposta formal a partir de um convite aceito.
    /// Reutiliza os mesmos campos de CriarRascunhoPropostaRequest.
    /// </summary>
    public class CriarPropostaContratanteRequest
    {
        public DateTime ValidoAte { get; set; }

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
