namespace Tratoo.Domain.Models.Financeiro
{
    /// <summary>
    /// Log de webhooks recebidos do gateway de pagamento.
    /// Usado para idempotência (evitar processamento duplicado) e auditoria.
    /// </summary>
    public class WebhookLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Chave de idempotência: "{tipoEvento}_{asaasCobrancaId}".
        /// Deve ser única — impede processar o mesmo evento duas vezes.
        /// </summary>
        public string ChaveIdempotencia { get; set; } = string.Empty;

        public string TipoEvento { get; set; } = string.Empty;

        /// <summary>ID da cobrança no Asaas (pay_...).</summary>
        public string? AsaasCobrancaId { get; set; }

        /// <summary>Payload JSON completo recebido do webhook.</summary>
        public string PayloadJson { get; set; } = string.Empty;

        public DateTime RecebidoEm { get; set; } = DateTime.UtcNow;

        public bool ProcessadoComSucesso { get; set; }

        /// <summary>Mensagem de erro caso o processamento tenha falhado.</summary>
        public string? ErroMensagem { get; set; }

        public DateTime? ProcessadoEm { get; set; }
    }
}
