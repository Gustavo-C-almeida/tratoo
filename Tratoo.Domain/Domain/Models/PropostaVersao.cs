namespace Tratoo.Domain.Models
{
    /// <summary>
    /// Conteúdo real de cada versão da proposta. Nunca é editado — apenas inserido.
    /// Garante histórico auditável da negociação.
    /// </summary>
    public class PropostaVersao
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PropostaId { get; set; }
        public PropostaProjeto Proposta { get; set; } = null!;

        /// <summary>Número sequencial (1, 2, 3…). Espelha PropostaProjeto.VersaoAtual no momento da criação.</summary>
        public int Versao { get; set; }

        /// <summary>Resumo do que será feito — alimenta o campo Objeto do contrato.</summary>
        public string Objetivo { get; set; } = string.Empty;

        /// <summary>Detalhamento do que está incluso no escopo.</summary>
        public string Escopo { get; set; } = string.Empty;

        /// <summary>O que explicitamente NÃO está incluso.</summary>
        public string? Exclusoes { get; set; }

        /// <summary>Quantidade de rodadas de revisão incluídas no valor.</summary>
        public int RevisoesInclusas { get; set; }

        /// <summary>Data limite de entrega conforme acordado nesta versão.</summary>
        public DateTime PrazoTotal { get; set; }

        /// <summary>Valor total da proposta em BRL.</summary>
        public decimal ValorTotal { get; set; }

        /// <summary>Valor de entrada (recomendado mínimo 30%).</summary>
        public decimal? Entrada { get; set; }

        /// <summary>Forma de pagamento. Padrão: PIX.</summary>
        public string FormaPagamento { get; set; } = "PIX";

        /// <summary>Informações adicionais — formato de entrega, ferramentas etc.</summary>
        public string? Observacoes { get; set; }

        /// <summary>Marcos/milestones em JSON. Formato: [{\"descricao\":\"...\",\"prazo\":\"...\",\"valor\":0.0}]</summary>
        public string? MarcosJson { get; set; }

        /// <summary>Id do usuário que criou esta versão (prestador ou contratante).</summary>
        public int CriadoPor { get; set; }

        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    }
}
