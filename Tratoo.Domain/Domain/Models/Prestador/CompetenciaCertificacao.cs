using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Models.Prestador
{
    public class CompetenciaCertificacao
    {
        public int CompetenciaId { get; set; }
        public Competencia Competencia { get; set; }

        public int CertificacaoPrestadorId { get; set; }
        public CertificacaoPrestador CertificacaoPrestador { get; set; }
    }
}
