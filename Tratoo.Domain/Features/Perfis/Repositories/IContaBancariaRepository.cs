using Tratoo.Domain.Models.Financeiro;

namespace Tratoo.Domain.Features.Perfis
{
    public interface IContaBancariaRepository
    {
        Task<ContaBancaria?> GetByPrestadorIdAsync(int prestadorId);
        Task AddAsync(ContaBancaria conta);
        Task SaveChangesAsync();
    }
}
