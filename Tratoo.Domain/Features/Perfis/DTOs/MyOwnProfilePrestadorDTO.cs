using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Perfis
{
    /// <summary>
    /// Visualização do próprio perfil pelo prestador.
    /// CPF/CNPJ NUNCA aparecem aqui — LGPD Art. 46.
    /// </summary>
    public class MyOwnProfilePrestadorDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // Campos do perfil vitrine
        public string? TituloProfissional { get; set; }
        public string? Descricao { get; set; }
        public string? FotoUrl { get; set; }
        public string? EmailContato { get; set; }

        /// <summary>JSON com até 3 links extras.</summary>
        public string? OutrosLinks { get; set; }

        public int PorcentagemCompleto { get; set; }

        // Campos do perfil mínimo (onboarding)
        public string? LinkedinUrl { get; set; }
        public string? PortfolioUrl { get; set; }
        public bool Disponivel { get; set; }
        public string? Telefone { get; set; }
        public string? AreaEspecializacao { get; set; }
        public string? FuncaoExecutada { get; set; }
        public string? LocalizacaoEstado { get; set; }
        public string? LocalizacaoCidade { get; set; }

        /// <summary>Nível de verificação: 1=Básico, 2=Identidade, 3=Financeiro.</summary>
        public NivelVerificacao? NivelVerificacao { get; set; }

        // Configurações de privacidade
        public bool AvaliacoesPrivado { get; set; }
        public bool DisponibilidadesPrivado { get; set; }

        public List<CompetenciaDTO> Competencias { get; set; } = new();
        public List<CertificacaoDTO> Certificacoes { get; set; } = new();
        public List<ExperienciaDTO> Experiencias { get; set; } = new();
        public List<PortfolioDTO> Portfolio { get; set; } = new();
    }
}
