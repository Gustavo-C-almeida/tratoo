using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Perfis
{
    /// <summary>
    /// Perfil básico do contratante.
    /// CNPJ NÃO é coletado aqui — será validado pelo IdentidadeService
    /// quando o contratante criar a primeira proposta.
    /// </summary>
    public class CompleteProfileContratanteDTO
    {
        public int ContratanteId { get; set; }

        /// <summary>PessoaFisica ou PessoaJuridica.</summary>
        public TipoPessoa? TipoPessoa { get; set; }

        public string? Segmento { get; set; }
        public string? NomeEmpresa { get; set; }
        public string? Cidade { get; set; }
        public string? Estado { get; set; }
    }
}
