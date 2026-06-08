namespace Tratoo.Domain.Enums
{
    public enum StatusPagamento
    {
        Criado,        // Registro criado no sistema, cobrança ainda não gerada no gateway
        Aguardando,    // Cobrança PIX gerada no Asaas, aguardando pagamento pelo contratante
        Processando,   // Gateway processando (cartão de crédito / análise de risco)
        Retido,        // Pago com sucesso — valor em escrow lógico na plataforma
        EmDisputa,     // Disputa aberta — liberação ao prestador suspensa
        TransferenciaEmProgresso, // Transferência PIX criada no Asaas, aguardando confirmação (TRANSFER_DONE)
        Liberado,      // Valor líquido transferido ao prestador via PIX (TRANSFER_DONE confirmado)
        FalhaTransferencia, // Transferência ao prestador falhou/cancelada (TRANSFER_FAILED/CANCELLED) — exige reprocessamento
        Cancelado,     // Cancelado antes do pagamento (contrato não executado)
        Estornado,     // Estorno processado pelo gateway
        Falhou         // Pagamento falhou no gateway
    }
}
