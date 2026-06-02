namespace Tratoo.API.Requests
{
    public class CriarConviteProjetoRequest
    {
        public int PrestadorId { get; set; }
        public string MensagemInicial { get; set; } = string.Empty;
        public decimal? OrcamentoSugerido { get; set; }
        public DateTime? PrazoDesejado { get; set; }
    }

    public class RecusarConviteRequest
    {
        public string? Motivo { get; set; }
    }
}
