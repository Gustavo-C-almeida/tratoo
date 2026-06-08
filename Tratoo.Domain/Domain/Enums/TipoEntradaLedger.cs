namespace Tratoo.Domain.Enums
{
    public enum TipoEntradaLedger
    {
        CobrancaCriada,
        CobrancaPaga,
        EscrowRetido,
        LiberacaoPrestador,
        EstornoSolicitado,
        EstornoConcluido,
        DisputaAberta,
        DisputaResolvidaContratante,
        DisputaResolvidaPrestador,

        /// <summary>Auditoria: contratante/admin solicitou a liberação (registra IP e dispositivo).</summary>
        LiberacaoSolicitada,

        /// <summary>Validação pré-transferência falhou (chave PIX inválida ou prestador inapto). Valor permanece retido.</summary>
        ValidacaoLiberacaoFalhou,
    }
}
