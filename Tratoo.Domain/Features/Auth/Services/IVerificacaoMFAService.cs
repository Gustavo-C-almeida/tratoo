namespace Tratoo.Domain.Features.Auth
{
    public interface IVerificacaoMFAService
    {
        Task<string> GerarECriarAsync(string email, string tipo);
        Task ValidarAsync(string email, string codigoDigitado, string tipo);
    }
}
