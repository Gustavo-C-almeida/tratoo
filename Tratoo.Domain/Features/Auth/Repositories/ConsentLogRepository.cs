using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public class ConsentLogRepository : IConsentLogRepository
    {
        private readonly TratooContext _db;

        public ConsentLogRepository(TratooContext db)
        {
            _db = db;
        }

        public async Task SalvarAsync(ConsentLog log)
        {
            _db.ConsentLogs.Add(log);
            await _db.SaveChangesAsync();
        }

        public async Task SalvarLoteAsync(IEnumerable<ConsentLog> logs)
        {
            _db.ConsentLogs.AddRange(logs);
            await _db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<ConsentLog>> ListarPorUserAsync(int userId)
            => await _db.ConsentLogs
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.CriadoEm)
                .ToListAsync();
    }
}
