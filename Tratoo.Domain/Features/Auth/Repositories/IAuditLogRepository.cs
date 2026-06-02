using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public interface IAuditLogRepository
    {
        Task RegistrarAsync(int userId, string acao, string ip);
    }
}
