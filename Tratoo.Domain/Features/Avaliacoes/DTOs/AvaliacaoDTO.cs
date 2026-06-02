using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Avaliacoes
{
    // ──────────────────────────────────────────────────────────────────────────
    // REQUEST
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Payload para enviar (submeter) uma avaliação pendente.</summary>
    public record EnviarAvaliacaoDto(
        int Nota,
        string? Comentario,
        bool Publica
    );

    /// <summary>Payload para responder publicamente a uma avaliação recebida (melhoria #2).</summary>
    public record ResponderAvaliacaoDto(string Resposta);

    // ──────────────────────────────────────────────────────────────────────────
    // RESPONSE
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Slot de avaliação pendente — exibido para o avaliador antes de submeter.</summary>
    public class AvaliacaoPendenteDto
    {
        public Guid Id { get; set; }
        public Guid ContratoServicoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        public string AvaliadoNome { get; set; } = string.Empty;
        public string AvaliadoFotoUrl { get; set; } = string.Empty;
        /// <summary>True quando o avaliador já preencheu sua nota (aguardando o outro lado).</summary>
        public bool JaEnviou { get; set; }
        public DateTime CriadoEm { get; set; }
    }

    /// <summary>Avaliação pública exibida no perfil do avaliado.</summary>
    public class AvaliacaoPublicaDto
    {
        public Guid Id { get; set; }
        public int AvaliadorId { get; set; }
        public int ProjetoId { get; set; }
        public int Nota { get; set; }
        public string? Comentario { get; set; }
        public string AvaliadorNome { get; set; } = string.Empty;
        public string AvaliadorFotoUrl { get; set; } = string.Empty;
        public string ProjetoTitulo { get; set; } = string.Empty;
        /// <summary>True se o avaliador é o contratante, False se é o prestador.</summary>
        public bool AvaliadorEhContratante { get; set; }
        public DateTime PublicadaEm { get; set; }
        public string? RespostaPublica { get; set; }
        public DateTime? RespostaPublicaCriadaEm { get; set; }
    }

    /// <summary>Reputação pública de um usuário (calculada a partir do RatingSummary).</summary>
    public class ReputacaoDto
    {
        public int UsuarioId { get; set; }
        public double MediaGeral { get; set; }
        public int TotalAvaliacoes { get; set; }
        public Dictionary<int, int> Distribuicao { get; set; } = [];
        /// <summary>True quando o usuário permite exibição pública de avaliações recebidas.</summary>
        public bool Publica { get; set; }
    }

    /// <summary>
    /// Resposta do endpoint público de avaliações.
    /// Sinaliza explicitamente a visibilidade (RN-AV-005): quando AvaliacoesVisiveis = false
    /// nenhum dado é retornado — o backend já filtrou na camada de serviço (RN-AV-011).
    /// </summary>
    public class AvaliacoesPublicasDto
    {
        public bool AvaliacoesVisiveis { get; set; }
        public List<AvaliacaoPublicaDto> Avaliacoes { get; set; } = [];
    }

    /// <summary>Item de avaliação nas listas "Minhas avaliações" (enviadas ou recebidas).</summary>
    public class MinhaAvaliacaoDto
    {
        public Guid Id { get; set; }
        public Guid ContratoServicoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        /// <summary>True = esta é uma avaliação que EU enviei (sou o avaliador).</summary>
        public bool SouAvaliador { get; set; }
        public string OutraParteNome { get; set; } = string.Empty;
        public int? Nota { get; set; }
        public string? Comentario { get; set; }
        public bool Publica { get; set; }
        public StatusAvaliacao Status { get; set; }
        /// <summary>Texto amigável do status no contexto do usuário (melhoria #1 — blind review visual).</summary>
        public string StatusDescricao { get; set; } = string.Empty;
        public DateTime? PublicadaEm { get; set; }
        public DateTime CriadoEm { get; set; }
    }
}
