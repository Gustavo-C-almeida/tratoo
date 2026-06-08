namespace Tratoo.Domain.Features.Auth
{
    public interface IJwtService
    {
        string Gerar(int usuarioId, string email, string nome, string tipo, bool perfilMinimoCompleto, bool isAdmin = false);
    }
}
