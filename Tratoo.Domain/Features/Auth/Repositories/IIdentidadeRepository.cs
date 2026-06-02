using Tratoo.Domain.Models;

namespace Tratoo.Domain.Features.Auth
{
    public interface IIdentidadeRepository
    {
        Task<UserIdentity?> ObterPorUserIdAsync(int userId);

        /// <summary>
        /// Verifica se o CPF/CNPJ criptografado já existe em outro UserIdentity ativo
        /// (anti-fraude: um documento não pode estar em duas contas).
        /// </summary>
        Task<bool> CpfCnpjJaExisteAsync(string cpfCnpjCriptografado);

        Task SalvarAsync(UserIdentity identity);
        Task AtualizarAsync(UserIdentity identity);
        Task RemoverAsync(UserIdentity identity);
    }
}
