using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Models.Prestador
{
    public class CompetenciaExperiencia
    {
        public int CompetenciaId { get; set; }
        public Competencia Competencia { get; set; }

        public int ExperienciaPrestadorId { get; set; }
        public ExperienciaPrestador ExperienciaPrestador { get; set; }
    }
}
