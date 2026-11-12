using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Mensagens
{
    public interface IConviteProjetoRepository
    {
        Task<ConviteProjeto?> GetByIdAsync(Guid id);
        Task<ConviteProjeto?> GetAtivoByPrestadorEProjetoAsync(int prestadorId, int projetoId);
        Task<List<ConviteProjeto>> GetDoPrestadorAsync(int prestadorId);
        Task<List<ConviteProjeto>> GetDoProjetoAsync(int projetoId);

        /// <summary>Marca como Expirado todo convite pendente vencido (prazo ou projeto fora de Aberto).</summary>
        Task<int> ExpirarVencidosAsync(DateTime agoraUtc);
        Task AddAsync(ConviteProjeto convite);
        Task SaveChangesAsync();
    }
}
