using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    public class Contratante : Usuario
    {
        public ICollection<Contrato> Contratos { get; set; } = new List<Contrato>();
        public ICollection<Proposta> PropostasEnviadas { get; set; } = new List<Proposta>();

        // ── PJ ────────────────────────────────────────────────────────────────
        public string? Segmento { get; set; }
        public string? NomeEmpresa { get; set; }
        public string? InscricaoEstadual { get; set; }
        public string? InscricaoMunicipal { get; set; }
        public DateOnly? DataAbertura { get; set; }

        // ── Perfil público ────────────────────────────────────────────────────
        public string? Descricao { get; set; }
        public string? LogoUrl { get; set; }
        public string? SiteUrl { get; set; }
        public string? LinkedinUrl { get; set; }

        // ── PF ────────────────────────────────────────────────────────────────
        /// <summary>Exibir idade publicamente no perfil (PF).</summary>
        public bool ExibirIdade { get; set; } = false;

        public bool PagadorVerificado { get; set; } = false;

        public override void VerificarPerfilMinimo()
        {
            // TipoPessoa, Endereco, IdentidadeVerificada herdados de Usuario
            // Para PJ também exige segmento e nome da empresa
            PerfilMinimoCompleto =
                TipoPessoa.HasValue &&
                IdentidadeVerificada &&
                !string.IsNullOrWhiteSpace(Endereco?.Cep) &&
                !string.IsNullOrWhiteSpace(Endereco?.Cidade) &&
                !string.IsNullOrWhiteSpace(Endereco?.Estado) &&
                (TipoPessoa == Enums.TipoPessoa.PessoaFisica ||
                 (!string.IsNullOrWhiteSpace(Segmento) && !string.IsNullOrWhiteSpace(NomeEmpresa)));
        }
    }
}
