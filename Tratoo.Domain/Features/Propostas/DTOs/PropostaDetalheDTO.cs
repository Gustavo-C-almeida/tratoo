using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Propostas
{
    public class PropostaDetalheDTO
    {
        public Guid Id { get; set; }
        public int ProjetoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        public int ContratanteId { get; set; }

        // Prestador
        public int PrestadorId { get; set; }
        public string PrestadorNome { get; set; } = string.Empty;
        public string? PrestadorFotoUrl { get; set; }
        public string? PrestadorTitulo { get; set; }

        /// <summary>Quem criou a proposta: Prestador (fluxo tradicional) ou Contratante (fluxo reverso).</summary>
        public PropostaSenderType SenderType { get; set; } = PropostaSenderType.Prestador;

        /// <summary>Convite que originou esta proposta (fluxo reverso).</summary>
        public Guid? ConviteId { get; set; }
        public bool FoiConvidado { get; set; }

        public StatusPropostaProjeto Status { get; set; }
        public int VersaoAtual { get; set; }
        public DateTime ValidoAte { get; set; }
        public string? MotivoCancelamento { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AtualizadoEm { get; set; }

        /// <summary>Versão ativa completa.</summary>
        public PropostaVersaoDTO? VersaoAtiva { get; set; }

        /// <summary>Histórico completo de versões (do mais antigo ao mais recente).</summary>
        public List<PropostaVersaoDTO> Versoes { get; set; } = new();
    }
}
