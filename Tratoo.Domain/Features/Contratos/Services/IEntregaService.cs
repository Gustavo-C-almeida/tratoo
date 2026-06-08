namespace Tratoo.Domain.Features.Contratos
{
    public interface IEntregaService
    {
        /// <summary>
        /// Prestador registra a entrega formal do serviço (descrição, anexos já enviados
        /// ao R2 e links). Move o contrato para AguardandoAprovacaoEntrega.
        /// </summary>
        Task<EntregaDetalheDto> RegistrarEntregaAsync(
            Guid contratoId, int prestadorId, RegistrarEntregaDto dto, List<EntregaAnexoUpload> anexos);

        /// <summary>
        /// Contratante aprova a entrega: contrato → Encerrado e dispara a liberação
        /// do pagamento retido ao prestador (reuso de IPagamentoService.LiberarPagamentoAsync).
        /// <paramref name="ip"/> e <paramref name="userAgent"/> são auditados (ação sensível).
        /// </summary>
        Task<EntregaDetalheDto> AprovarEntregaAsync(Guid contratoId, int contratanteId, string? ip, string? userAgent);

        /// <summary>
        /// Contratante solicita ajustes: entrega → Rejeitada e contrato volta a Ativo.
        /// </summary>
        Task<EntregaDetalheDto> RejeitarEntregaAsync(Guid contratoId, int contratanteId, string motivo);

        /// <summary>Retorna a entrega atual do contrato (anexos com URL pré-assinada). Null se não houver.</summary>
        Task<EntregaDetalheDto?> ObterEntregaAsync(Guid contratoId, int usuarioId);
    }
}
