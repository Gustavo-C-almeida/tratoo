using System;
using System.Collections.Generic;

namespace Tratoo.Domain.Models
{
    public class Contrato
    {
        public Guid? Id { get; set; }

        // ======================
        // Origem
        // ======================
        public Guid? PropostaId { get; set; }
        public Proposta? Proposta { get; set; }

        // ======================
        // Envolvidos
        // ======================
        public int? PrestadorId { get; set; }
        public Prestador.Prestador? Prestador { get; set; }

        public int? ContratanteId { get; set; }
        public Contratante? Contratante { get; set; }

        // ======================
        // Financeiro
        // ======================
        public decimal? ValorBruto { get; set; }
        public decimal? TaxaPlataforma { get; set; }

        public decimal? ValorLiquidoPrestador =>
            ValorBruto.HasValue && TaxaPlataforma.HasValue
                ? ValorBruto - TaxaPlataforma
                : null;

        // ======================
        // Status
        // ======================
        public StatusContrato? Status { get; set; } = StatusContrato.AguardandoPagamento;

        // ======================
        // Datas
        // ======================
        public DateTime? CriadoEm { get; set; } = DateTime.UtcNow;
        public DateTime? PagoEm { get; set; }
        public DateTime? IniciadoEm { get; set; }
        public DateTime? ConcluidoEm { get; set; }

        // ======================
        // SLA / Prazo
        // ======================
        public DateTime? PrazoEntrega { get; set; }

        // ======================
        // Pagamentos
        // ======================
        public ICollection<Pagamento>? Pagamentos { get; set; } = new List<Pagamento>();

        // ======================
        // CAMPANHA INFLUENCER
        // ======================

        // O que será entregue
        public int? QuantidadePosts { get; set; }
        public int? QuantidadeStories { get; set; }
        public int? QuantidadeReels { get; set; }
        public int? QuantidadeVideos { get; set; }

        // Regras do conteúdo
        public string? PlataformaPrincipal { get; set; } // Instagram, TikTok, YouTube
        public string? DiretrizesConteudo { get; set; }  // briefing
        public bool? ExigeAprovacaoPrevia { get; set; }

        // Direitos
        public bool? PermiteUsoImagem { get; set; }
        public int? PrazoUsoImagemDias { get; set; }
        public bool? ExclusividadeSegmento { get; set; }

        // Penalidades / proteção
        public bool? MultaPorNaoEntrega { get; set; }
        public decimal? ValorMulta { get; set; }
    }

    public enum StatusContrato
    {
        AguardandoPagamento,
        EmAndamento,
        Concluido,
        Cancelado,
        EmDisputa
    }
}
