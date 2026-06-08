using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Perfis
{
    /// <summary>
    /// Perfil público do prestador. Não expõe CPF/CNPJ nem dados sensíveis — LGPD Art. 46.
    /// </summary>
    public class PerfilPublicoDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? TituloProfissional { get; set; }

        /// <summary>Mapeado de Prestador.Descricao.</summary>
        public string? Bio { get; set; }

        public string? FotoUrl { get; set; }
        public string? AreaEspecializacao { get; set; }
        public string? FuncaoExecutada { get; set; }
        public bool Disponivel { get; set; }
        public string? LinkedinUrl { get; set; }
        public string? PortfolioUrl { get; set; }

        /// <summary>JSON com até 3 links extras: [{\"titulo\":\"X\",\"url\":\"Y\"}]</summary>
        public string? OutrosLinks { get; set; }

        public string? LocalizacaoCidade { get; set; }
        public string? LocalizacaoEstado { get; set; }
        public int PorcentagemCompleto { get; set; }
        public NivelVerificacao? NivelVerificacao { get; set; }

        public List<CompetenciaDTO> Competencias { get; set; } = new();
        public List<CertificacaoDTO> Certificacoes { get; set; } = new();
        public List<ExperienciaDTO> Experiencias { get; set; } = new();
        public List<PortfolioDTO> Portfolio { get; set; } = new();
    }
}
