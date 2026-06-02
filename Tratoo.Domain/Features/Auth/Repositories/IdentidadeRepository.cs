using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public class IdentidadeRepository : IIdentidadeRepository
    {
        private readonly TratooContext _db;

        public IdentidadeRepository(TratooContext db)
        {
            _db = db;
        }

        public async Task<UserIdentity?> ObterPorUserIdAsync(int userId)
            => await _db.UserIdentities.FirstOrDefaultAsync(i => i.UserId == userId);

        public async Task<bool> CpfCnpjJaExisteAsync(string cpfCnpjCriptografado)
            => await _db.UserIdentities.AnyAsync(i => i.CpfCnpjCriptografado == cpfCnpjCriptografado);

        public async Task SalvarAsync(UserIdentity identity)
        {
            _db.UserIdentities.Add(identity);
            await _db.SaveChangesAsync();
        }

        public async Task AtualizarAsync(UserIdentity identity)
        {
            _db.UserIdentities.Update(identity);
            await _db.SaveChangesAsync();
        }

        public async Task RemoverAsync(UserIdentity identity)
        {
            _db.UserIdentities.Remove(identity);
            await _db.SaveChangesAsync();
        }
    }
}
