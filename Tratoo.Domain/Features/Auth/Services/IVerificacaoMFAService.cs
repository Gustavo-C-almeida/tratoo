namespace Tratoo.Domain.Features.Auth
{
    public interface IVerificacaoMFAService
    {
        string GerarECriar(string email, string tipo);
        void Validar(string email, string codigoDigitado, string tipo);
    }
}
