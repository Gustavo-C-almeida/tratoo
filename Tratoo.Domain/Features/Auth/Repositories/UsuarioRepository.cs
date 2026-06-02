using Microsoft.EntityFrameworkCore;
using Tratoo.Domain.Data;
using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public class UsuarioRepository : IUsuarioRepository
    {
        private readonly TratooContext _db;

        public UsuarioRepository(TratooContext db)
        {
            _db = db;
        }

        public Task<bool> EmailExisteAsync(string email)
            => _db.Usuarios.AnyAsync(u => u.Email == email);

        public async Task SalvarAsync(Usuario user)
        {
            _db.Usuarios.Add(user);
            await _db.SaveChangesAsync();
        }

        public async Task AtualizarAsync(Usuario user)
        {
            _db.Usuarios.Update(user);
            await _db.SaveChangesAsync();
        }

        public async Task<Usuario?> ObterPorEmailAsync(string email)
            => await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email);

        public async Task<Usuario?> ObterPorIdAsync(int id)
            => await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == id);
    }
}
