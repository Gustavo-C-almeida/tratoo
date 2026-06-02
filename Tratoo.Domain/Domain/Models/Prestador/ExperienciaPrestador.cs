using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Models.Prestador
{
    public class ExperienciaPrestador
    {
        public int Id { get; set; }
        public int PrestadorId { get; set; }

        public Prestador Prestador { get; set; }

        public string Empresa { get; set; }
        public string Cargo { get; set; }
        public string? Atividades { get; set; }

        public DateTime DataInicio { get; set; }
        public DateTime? DataFim { get; set; }

        public bool EmpregoAtual { get; set; }

        public string? Local { get; set; }
        public string? TipoContrato { get; set; }

        public ICollection<CompetenciaExperiencia> CompetenciaExperiencias { get; set; }

    }
}
