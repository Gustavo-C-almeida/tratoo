using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tratoo.Domain.Models.Prestador
{
    public class CertificacaoPrestador
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string InstituicaoEmissora { get; set; }

        public DateTime DataEmissao { get; set; }
        public DateTime? DataValidade { get; set; }

        /// <summary>Link para verificação/validação do certificado.</summary>
        public string? LinkVerificacao { get; set; }

        public int PrestadorId { get; set; }
        public Prestador Prestador { get; set; }
        public ICollection<CompetenciaCertificacao> CompetenciaCertificacoes { get; set; }
    }
}
