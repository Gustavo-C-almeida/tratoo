namespace Tratoo.Domain.Features.Perfis
{
    /// <summary>Projeto resumido exibido no perfil público do contratante.</summary>
    public class ProjetoResumoPerfilDto
    {
        public int Id { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal OrcamentoMin { get; set; }
        public decimal OrcamentoMax { get; set; }
        public DateTime? PublicadoEm { get; set; }
        public DateTime PrazoEntrega { get; set; }
        public int TotalPropostas { get; set; }
    }

    /// <summary>Perfil público do contratante — sem dados sensíveis.</summary>
    public class PerfilPublicoContratanteDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string? LogoUrl { get; set; }
        public string? SiteUrl { get; set; }
        public string? LinkedinUrl { get; set; }
        public string? Segmento { get; set; }
        public string? NomeEmpresa { get; set; }
        public string? LocalizacaoCidade { get; set; }
        public string? LocalizacaoEstado { get; set; }
        /// <summary>Idade calculada a partir da data de nascimento — só presente se ExibirIdade = true.</summary>
        public int? Idade { get; set; }
        /// <summary>Data de cadastro na plataforma.</summary>
        public DateTime CriadoEm { get; set; }

        // ── Métricas públicas ────────────────────────────────────────────────
        public int TotalProjetosPublicados { get; set; }
        public int TotalProjetosConcluidos { get; set; }
        /// <summary>Percentual (0–100) de projetos publicados que foram concluídos.</summary>
        public int TaxaConclusao { get; set; }
        public double? MediaAvaliacoes { get; set; }
        public int TotalAvaliacoes { get; set; }
        public decimal? ValorMedioProjetos { get; set; }
        /// <summary>True quando CPF/CNPJ foi validado (NivelVerificacao >= Identidade).</summary>
        public bool EmpresaVerificada { get; set; }

        /// <summary>Até 5 projetos mais recentes publicados pelo contratante.</summary>
        public List<ProjetoResumoPerfilDto> UltimosProjetos { get; set; } = [];
    }

    /// <summary>Perfil completo do contratante logado — inclui campos privados.</summary>
    public class MeuPerfilContratanteDTO
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string? LogoUrl { get; set; }
        public string? SiteUrl { get; set; }
        public string? LinkedinUrl { get; set; }
        public string? Telefone { get; set; }
        public string? Segmento { get; set; }
        public string? NomeEmpresa { get; set; }
        public string? InscricaoEstadual { get; set; }
        public string? InscricaoMunicipal { get; set; }
        public DateOnly? DataAbertura { get; set; }
        public string? LocalizacaoCidade { get; set; }
        public string? LocalizacaoEstado { get; set; }
        public bool ExibirIdade { get; set; }
        public int? Idade { get; set; }
        public string? TipoPessoa { get; set; }
        /// <summary>Quando true, avaliações não são exibidas publicamente (RN-AV-001).</summary>
        public bool AvaliacoesPrivado { get; set; }

        // ── Completude ────────────────────────────────────────────────────────
        public int PorcentagemCompletude { get; set; }
        public string? ProximoPassoCompletude { get; set; }
    }

    /// <summary>Payload para atualizar o perfil complementar do contratante.</summary>
    public class AtualizarPerfilContratanteDTO
    {
        public int ContratanteId { get; set; }
        public string? Descricao { get; set; }
        public string? SiteUrl { get; set; }
        public string? LinkedinUrl { get; set; }
        public string? Telefone { get; set; }
        public bool ExibirIdade { get; set; }
    }
}
