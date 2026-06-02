namespace Tratoo.API.Requests
{
    public class EnviarPropostaProjetoRequest
    {
        public decimal Valor { get; set; }
        public DateTime PrazoEntrega { get; set; }
        public string CartaApresentacao { get; set; } = string.Empty;
    }
}
