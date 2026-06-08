using Tratoo.Domain.Enums;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Propostas
{
    /// <summary>Item da lista de propostas exibida ao contratante.</summary>
    public class PropostaResumoDTO
    {
        public Guid Id { get; set; }
        public int ProjetoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        public int PrestadorId { get; set; }
        public string PrestadorNome { get; set; } = string.Empty;
        public string? PrestadorFotoUrl { get; set; }
        public string? PrestadorTitulo { get; set; }
        public StatusPropostaProjeto Status { get; set; }
        public PropostaSenderType SenderType { get; set; } = PropostaSenderType.Prestador;
        public Guid? ConviteId { get; set; }
        public bool FoiConvidado { get; set; }
        public int VersaoAtual { get; set; }
        public DateTime ValidoAte { get; set; }
        public DateTime CriadoEm { get; set; }

        // Dados da versão ativa (para exibir na lista sem carregar tudo)
        public decimal ValorTotal { get; set; }
        public DateTime PrazoTotal { get; set; }
        public string Objetivo { get; set; } = string.Empty;
        public int RevisoesInclusas { get; set; }
    }
}
