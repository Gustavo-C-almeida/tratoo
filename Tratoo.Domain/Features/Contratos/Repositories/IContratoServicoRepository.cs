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
        Task AddAsync(ContratoServico contrato);
        Task AddSnapshotAsync(ContratoSnapshot snapshot);
        Task SaveChangesAsync();
    }
}
