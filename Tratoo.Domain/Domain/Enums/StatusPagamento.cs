namespace Tratoo.Domain.Enums
{
    public enum StatusPagamento
    {
        Criado,        // Registro criado no sistema, cobrança ainda não gerada no gateway
        Aguardando,    // Cobrança PIX gerada no Asaas, aguardando pagamento pelo contratante
        Processando,   // Gateway processando (cartão de crédito / análise de risco)
        Retido,        // Pago com sucesso — valor em escrow lógico na plataforma
        EmDisputa,     // Disputa aberta — liberação ao prestador suspensa
        Liberado,      // Valor líquido transferido ao prestador via PIX
        Cancelado,     // Cancelado antes do pagamento (contrato não executado)
        Estornado,     // Estorno processado pelo gateway
        Falhou         // Pagamento falhou no gateway
    }
}
