using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Contratos
{
    public interface IContratoServicoRepository
    {
        Task<ContratoServico?> GetByIdAsync(Guid id);
        Task<ContratoServico?> GetByPropostaIdAsync(Guid propostaId);
        Task<List<ContratoServico>> GetDoContratanteAsync(int contratanteId);
        Task<List<ContratoServico>> GetDoPrestadorAsync(int prestadorId);
        Task<List<ContratoServico>> GetExpiradosAsync(DateTime agora);

        /// <summary>
        /// Cancela o contrato por expiração (e recusa a proposta / reabre o projeto) só se
        /// ele ainda estiver pendente e vencido no momento da escrita. false = outro
        /// processo ou o usuário já mudou o contrato; nada foi alterado.
        /// </summary>
        Task<bool> CancelarPorExpiracaoAsync(
            Guid contratoId, Guid propostaId, int projetoId, DateTime agora,
            string motivoContrato, string motivoProposta);
        Task AddAsync(ContratoServico contrato);
        Task AddSnapshotAsync(ContratoSnapshot snapshot);
        Task AddHistoricoAsync(HistoricoAssinatura historico);
        Task SaveChangesAsync();
    }
}
