namespace Tratoo.API.Requests
{
    /// <param name="ManterConectado">Repetido do primeiro passo: a sessão só nasce aqui.</param>
    public record ValidarLoginMFARequest(string Email, string Codigo, bool ManterConectado = false);
}
