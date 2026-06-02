using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly TratooContext _db;

        public AuditLogRepository(TratooContext db)
        {
            _db = db;
        }

        public async Task RegistrarAsync(int userId, string acao, string ip)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Acao = acao,
                Ip = ip,
                DataHora = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
    }
}
