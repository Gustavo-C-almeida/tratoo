
namespace Tratoo.Domain.Features.Auth
{
    public interface ILoginService
    {
        Task<LoginResponseDTO> AutenticarAsync(LoginDTO dto);
        Task<LoginResponseDTO> ValidarMFAAsync(ValidarLoginMFAUserDTO dto);
        Task SolicitarResetSenhaAsync(string email, string ip);
        Task ResetarSenhaAsync(ResetarSenhaDTO dto);
    }
}
