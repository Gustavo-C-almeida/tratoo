using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Propostas
{
    public interface IPropostaProjetoRepository
    {
        Task<PropostaProjeto?> GetByIdAsync(Guid id);
        Task<PropostaProjeto?> GetByIdComVersoesAsync(Guid id);
        Task<PropostaProjeto?> GetAtivaByPrestadorEProjetoAsync(int prestadorId, int projetoId);
        Task<PropostaProjeto?> GetAtivaByConviteIdAsync(Guid conviteId);
        Task<List<PropostaProjeto>> GetDoProjetoAsync(int projetoId);
        Task<List<PropostaProjeto>> GetDoPrestadorAsync(int prestadorId);
        Task<List<PropostaProjeto>> GetExpiradas(DateTime agora);
        Task AddAsync(PropostaProjeto proposta);
        Task AddVersaoAsync(PropostaVersao versao);
        Task<PropostaVersao?> GetVersaoAsync(Guid propostaId, int numeroVersao);
        Task SaveChangesAsync();
    }
}
