
namespace Tratoo.Domain.Features.Auth
{
    public interface ICadastroService
    {
        /// <summary>Etapa 1: registra dados, envia código de confirmação por e-mail.</summary>
        Task CadastrarAsync(CadastroUserDTO dto);

        /// <summary>Etapa 2: confirma e-mail, cria conta e grava ConsentLog.</summary>
        Task ConfirmarCadastroAsync(ConfirmarCadastroUserDTO dto);

        /// <summary>Reenvia o código de verificação, respeitando o cooldown de 1 minuto.</summary>
        Task ReenviarCodigoAsync(string email);
    }
}
