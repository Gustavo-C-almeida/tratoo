namespace Tratoo.API.Requests
{
    public class AdicionarCertificacaoRequest
    {
        public string    Nome             { get; set; } = string.Empty;
        public string    Instituicao      { get; set; } = string.Empty;
        public DateTime  DataEmissao      { get; set; }
        public DateTime? DataValidade     { get; set; }
        public string?   LinkVerificacao  { get; set; }
    }

    public class AtualizarCertificacaoRequest
    {
        public string    Nome             { get; set; } = string.Empty;
        public string    Instituicao      { get; set; } = string.Empty;
        public DateTime  DataEmissao      { get; set; }
        public DateTime? DataValidade     { get; set; }
        public string?   LinkVerificacao  { get; set; }
    }
}
