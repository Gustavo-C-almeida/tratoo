namespace Tratoo.Domain.Features.Auth
{
    public interface IJwtService
    {
        /// <summary>
        /// Access token curto. <paramref name="sid"/> liga o token à sessão no servidor
        /// (validada a cada requisição); <paramref name="validade"/> vem da política da sessão
        /// (15 min, ou 10 min para Admin).
        /// </summary>
        string Gerar(int usuarioId, string email, string nome, string tipo, bool perfilMinimoCompleto, bool isAdmin, string sid, TimeSpan validade);
    }
}
