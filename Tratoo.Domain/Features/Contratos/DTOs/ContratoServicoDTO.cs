using Tratoo.Domain.Models;
using Tratoo.Domain.Enums;

namespace Tratoo.Domain.Features.Contratos
{
    // ── Partes do contrato ─────────────────────────────────────────────────────

    public class ContratoParteDto
    {
        public string Nome { get; set; } = string.Empty;
        /// <summary>CPF ou CNPJ mascarado (ex: "***.***.123-**").</summary>
        public string CpfCnpjMascarado { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Endereco { get; set; } = string.Empty;
    }

    // ── Escopo ────────────────────────────────────────────────────────────────

    public class ContratoEscopoDto
    {
        public string Entregaveis { get; set; } = string.Empty;
        public int Revisoes { get; set; }
        public string FormatoEntrega { get; set; } = string.Empty;
        public string OQueNaoEstaIncluso { get; set; } = string.Empty;
    }

    // ── Marco de projeto ─────────────────────────────────────────────────────

    public class ContratoMarcoDto
    {
        public string Descricao { get; set; } = string.Empty;
        public DateTime Prazo { get; set; }
        public decimal Valor { get; set; }
    }

    // ── Prazo ─────────────────────────────────────────────────────────────────

    public class ContratoPrazoDto
    {
        public DateTime DataInicio { get; set; }
        public DateTime DataTermino { get; set; }
        public List<ContratoMarcoDto> Marcos { get; set; } = new();
    }

    // ── Pagamento ─────────────────────────────────────────────────────────────

    public class ContratoPagamentoDto
    {
        public decimal ValorTotal { get; set; }
        public decimal Entrada { get; set; } = 0;
        public string FormaPagamento { get; set; } = "PIX";
        public string MultaAtraso { get; set; } = "2% ao mês + IPCA";
    }

    // ── Conteúdo completo do contrato (editável antes de assinar) ─────────────

    public class ContratoConteudoDto
    {
        public ContratoParteDto Contratante { get; set; } = new();
        public ContratoParteDto Prestador { get; set; } = new();
        public string Objeto { get; set; } = string.Empty;
        public ContratoEscopoDto Escopo { get; set; } = new();
        public ContratoPrazoDto Prazo { get; set; } = new();
        public ContratoPagamentoDto Pagamento { get; set; } = new();
        public string DireitosAutorais { get; set; } = "Transferidos ao contratante após pagamento integral (Lei 9.610/98, Art. 4).";
        public string Confidencialidade { get; set; } = "Ambas as partes se comprometem a manter sigilo por 2 anos após o encerramento do contrato.";
        public string Cancelamento { get; set; } = "Aviso prévio de 7 dias. Multa de 20% sobre o valor total em caso de quebra injustificada.";
        public string Foro { get; set; } = "Comarca de São Paulo - SP";
    }

    // ── DTOs de resposta ──────────────────────────────────────────────────────

    public class ContratoResumoDto
    {
        public Guid Id { get; set; }
        public int ProjetoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        public int ContratanteId { get; set; }
        public string ContratanteNome { get; set; } = string.Empty;
        public int PrestadorId { get; set; }
        public string PrestadorNome { get; set; } = string.Empty;
        public ContratoServicoStatus Status { get; set; }
        public decimal ValorTotal { get; set; }
        public DateTime DataTermino { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime ExpiraEm { get; set; }
        public bool AssinadoPorMim { get; set; }
        public bool AssinadoPelaOutraParte { get; set; }
    }

    public class ContratoDetalheDto
    {
        public Guid Id { get; set; }
        public int ProjetoId { get; set; }
        public string ProjetoTitulo { get; set; } = string.Empty;
        public Guid PropostaId { get; set; }
        public int ContratanteId { get; set; }
        public string ContratanteNome { get; set; } = string.Empty;
        public int PrestadorId { get; set; }
        public string PrestadorNome { get; set; } = string.Empty;

        public ContratoServicoStatus Status { get; set; }

        public ContratoConteudoDto Conteudo { get; set; } = new();

        public DateTime? AssinadoContratanteEm { get; set; }
        public DateTime? AssinadoPrestadorEm { get; set; }
        public string? ConteudoHash { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime ExpiraEm { get; set; }

        /// <summary>Indica se o usuário atual já assinou.</summary>
        public bool AssinadoPorMim { get; set; }
        /// <summary>Indica se a outra parte já assinou.</summary>
        public bool AssinadoPelaOutraParte { get; set; }
        /// <summary>True quando o PDF foi gerado e está disponível para download.</summary>
        public bool TemPdf { get; set; }

        /// <summary>Data em que o prestador registrou a entrega. Null se ainda não entregou.</summary>
        public DateTime? EntregaRegistradaEm { get; set; }
        /// <summary>True quando o prestador já registrou entrega — impede cancelamento direto.</summary>
        public bool TemEntregaRegistrada { get; set; }

        /// <summary>True quando o contrato ainda pode ser cancelado diretamente (sem disputa).</summary>
        public bool PodeCancelar { get; set; }

        // ── Estado do pagamento em garantia (escrow) ──────────────────────────────
        /// <summary>Id do pagamento associado ao contrato (null se ainda não iniciado).</summary>
        public Guid? PagamentoId { get; set; }
        /// <summary>Status atual do pagamento/escrow. Null se nenhum pagamento foi iniciado.</summary>
        public StatusPagamento? StatusPagamento { get; set; }
        /// <summary>
        /// True quando o valor está protegido em escrow (pago e retido na plataforma).
        /// Pré-requisito para o prestador registrar a entrega.
        /// </summary>
        public bool PagamentoConfirmado { get; set; }

        public string? MotivoCancelamento { get; set; }
        public DateTime? CanceladoEm { get; set; }

        /// <summary>Versão do template de contrato usado na geração.</summary>
        public string? TemplateVersao { get; set; }
    }
}
