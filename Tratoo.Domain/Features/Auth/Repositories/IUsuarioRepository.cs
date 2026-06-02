using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public interface IUsuarioRepository
    {
        Task<bool> EmailExisteAsync(string email);
        Task SalvarAsync(Usuario user);
        Task AtualizarAsync(Usuario user);
        Task<Usuario?> ObterPorEmailAsync(string email);
        Task<Usuario?> ObterPorIdAsync(int id);
    }
}
