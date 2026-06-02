namespace Tratoo.API.Requests
{
    public class AdicionarExperienciaRequest
    {
        public string  Empresa      { get; set; } = string.Empty;
        public string  Cargo        { get; set; } = string.Empty;
        public string? Atividades   { get; set; }
        public DateTime DataInicio  { get; set; }
        public DateTime? DataFim    { get; set; }
        public bool EmpregoAtual    { get; set; }
        public string? Local        { get; set; }
        public string? TipoContrato { get; set; }
    }

    public class AtualizarExperienciaRequest
    {
        public string  Empresa      { get; set; } = string.Empty;
        public string  Cargo        { get; set; } = string.Empty;
        public string? Atividades   { get; set; }
        public DateTime DataInicio  { get; set; }
        public DateTime? DataFim    { get; set; }
        public bool EmpregoAtual    { get; set; }
        public string? Local        { get; set; }
        public string? TipoContrato { get; set; }
    }
}
