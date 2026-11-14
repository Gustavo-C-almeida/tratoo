namespace Tratoo.Domain.Features.Pagamentos
{
    /// <summary>Desfecho de um estorno já reservado (pagamento em EstornoEmProgresso).</summary>
    public enum ResultadoEstorno
    {
        /// <summary>O Asaas aceitou e o banco registrou: pagamento Estornado, com ledger.</summary>
        Estornado,

        /// <summary>
        /// O Asaas aceitou, mas gravar falhou. O pagamento continua EstornoEmProgresso — nunca
        /// liberável — até o webhook PAYMENT_REFUNDED (ou conciliação) concluir.
        /// </summary>
        EstornadoSemRegistro,

        /// <summary>
        /// Não se sabe se o Asaas estornou (rede, timeout, 5xx...). O pagamento continua
        /// EstornoEmProgresso, marcado para conciliação, sem nova tentativa automática.
        /// </summary>
        Incerto,

        /// <summary>
        /// O Asaas recusou de forma comprovada (ou não havia cobrança para estornar): nada foi
        /// estornado. Só neste caso o chamador pode desfazer a reserva (de volta a Retido).
        /// </summary>
        FalhaDefinitiva
    }
}
