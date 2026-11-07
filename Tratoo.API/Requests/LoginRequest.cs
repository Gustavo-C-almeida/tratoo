namespace Tratoo.API.Requests
{
    /// <param name="ManterConectado">Sessão de 14 dias com 7 de inatividade (não vale para Admin).</param>
    public record LoginRequest(string Email, string Senha, bool ManterConectado = false);
}
