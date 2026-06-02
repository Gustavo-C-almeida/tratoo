namespace Tratoo.Domain.Features.Perfis
{
    public class CompetenciaDTO
    {
        public int Id { get; set; }
        public int PrestadorId { get; set; }
        public string Nome { get; set; } = string.Empty;
        /// <summary>Nível de proficiência: 1 (Básico) a 5 (Especialista).</summary>
        public int Nivel { get; set; }
    }
}
