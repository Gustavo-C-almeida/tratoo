namespace Tratoo.API.Requests
{
    public class AdicionarPortfolioRequest
    {
        public string Titulo       { get; set; } = string.Empty;
        public string? Descricao   { get; set; }
        public string? LinkExterno { get; set; }
        // ArquivoUrl é preenchido pelo endpoint após upload ao R2
    }

    public class AtualizarPortfolioRequest
    {
        public string Titulo       { get; set; } = string.Empty;
        public string? Descricao   { get; set; }
        public string? LinkExterno { get; set; }
        // ArquivoUrl é preenchido pelo endpoint após upload ao R2
    }
}
