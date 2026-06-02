using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Models
{
    public enum StatusPropostaProjeto
    {
        /// <summary>Rascunho — preenchido mas não enviado. Editável livremente.</summary>
        Draft,
        /// <summary>Enviado — aguardando análise do contratante.</summary>
        Submitted,
        /// <summary>Em negociação ativa (após contraproposta ou mensagem de ajuste).</summary>
        EmNegociacao,
        /// <summary>Aceita — disparou geração de contrato.</summary>
        Aceita,
        /// <summary>Recusada — estado terminal negativo.</summary>
        Recusada,
        /// <summary>Prazo vencido sem aceite.</summary>
        Expirada,
        /// <summary>Convertida em contrato — estado terminal positivo.</summary>
        Convertida
    }

    public class PropostaProjeto
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public int ProjetoId { get; set; }
        public Projeto Projeto { get; set; } = null!;

        public int PrestadorId { get; set; }
        public Prestador.Prestador Prestador { get; set; } = null!;

        /// <summary>Quem criou a proposta: Prestador (fluxo tradicional) ou Contratante (fluxo reverso pós-convite).</summary>
        public PropostaSenderType SenderType { get; set; } = PropostaSenderType.Prestador;

        /// <summary>Convite que originou esta proposta (apenas no fluxo reverso).</summary>
        public Guid? ConviteId { get; set; }
        public ConviteProjeto? Convite { get; set; }

        public StatusPropostaProjeto Status { get; set; } = StatusPropostaProjeto.Draft;

        /// <summary>Número da versão ativa. Começa em 1, incrementa a cada negociação.</summary>
        public int VersaoAtual { get; set; } = 1;

        /// <summary>Proposta expira automaticamente se não aceita até essa data.</summary>
        public DateTime ValidoAte { get; set; }

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
        public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

        public string? MotivoCancelamento { get; set; }
        public int? CanceladoPorId { get; set; }
        public DateTime? CanceladoEm { get; set; }

        public ICollection<PropostaVersao> Versoes { get; set; } = new List<PropostaVersao>();
    }
}
