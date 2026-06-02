namespace Tratoo.Domain.Features.Perfis
{
    public class ExperienciaDTO
    {
        public int Id { get; set; }
        public int PrestadorId { get; set; }

        public string Empresa { get; set; } = string.Empty;
        public string Cargo { get; set; } = string.Empty;
        public string? Atividades { get; set; }

        public DateTime DataInicio { get; set; }
        public DateTime? DataFim { get; set; }

        public bool EmpregoAtual { get; set; }

        public string? Local { get; set; }
        public string? TipoContrato { get; set; }

        public List<CompetenciaDTO> Competencias { get; set; } = new();
    }
}
