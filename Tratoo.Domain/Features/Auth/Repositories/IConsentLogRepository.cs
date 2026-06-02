using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public interface IConsentLogRepository
    {
        Task SalvarAsync(ConsentLog log);
        Task SalvarLoteAsync(IEnumerable<ConsentLog> logs);
        Task<IReadOnlyList<ConsentLog>> ListarPorUserAsync(int userId);
    }
}
