using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Models.Prestador
{
    public class Prestador : Usuario
    {
        public ICollection<Competencia> Competencias { get; set; } = new List<Competencia>();
        public ICollection<CertificacaoPrestador> Certificacoes { get; set; } = new List<CertificacaoPrestador>();
        public ICollection<ExperienciaPrestador> Experiencias { get; set; } = new List<ExperienciaPrestador>();
        public ICollection<DisponibilidadeHorario> Disponibilidades { get; set; } = new List<DisponibilidadeHorario>();
        public ICollection<PortfolioPrestador> Portfolio { get; set; } = new List<PortfolioPrestador>();

        public ContaBancaria? ContaBancaria { get; set; }

        // ── Identidade profissional ────────────────────────────────────────────
        public string? NomeFantasia { get; set; }

        public string? AreaEspecializacao { get; set; }
        public string? FuncaoExecutada { get; set; }
        public string? Descricao { get; set; }

        // Links
        public string? LinkedinUrl { get; set; }
        public string? PortfolioUrl { get; set; }

        // ── Perfil Vitrine ────────────────────────────────────────────────────
        /// <summary>Título profissional exibido no perfil (ex: "Desenvolvedor Full-Stack Senior").</summary>
        public string? TituloProfissional { get; set; }

        /// <summary>URL da foto de perfil armazenada no Cloudflare R2.</summary>
        public string? FotoUrl { get; set; }

        /// <summary>E-mail de contato público (pode diferir do e-mail de login).</summary>
        public string? EmailContato { get; set; }

        /// <summary>JSON com até 3 links extras: [{"titulo":"X","url":"Y"}]</summary>
        public string? OutrosLinks { get; set; }

        /// <summary>Percentual de completude do perfil vitrine (0–100), recalculado a cada atualização.</summary>
        public int PorcentagemCompleto { get; set; } = 0;

        // ── Disponibilidade ───────────────────────────────────────────────────
        public bool Disponivel { get; set; } = true;
        public DateTime? DisponivelAPartirDe { get; set; }

        // ── Financeiro ────────────────────────────────────────────────────────
        public decimal? ValorMinimoProjeto { get; set; }
        public bool? AceitaParcelamento { get; set; } = true;

        // ── Privacidade ───────────────────────────────────────────────────────
        public bool DisponibilidadesPrivado { get; set; } = false;

        public override void VerificarPerfilMinimo()
        {
            // TipoPessoa, Endereco herdados de Usuario
            PerfilMinimoCompleto =
                TipoPessoa.HasValue &&
                IdentidadeVerificada &&
                !string.IsNullOrWhiteSpace(Endereco?.Cep) &&
                !string.IsNullOrWhiteSpace(Endereco?.Cidade) &&
                !string.IsNullOrWhiteSpace(Endereco?.Estado);
        }
    }
}
