namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Sessão de login guardada no servidor (chave lógica <c>sessao:{sid}</c>). Fica no
    /// Redis, cifrada: perder uma sessão só obriga a um novo login, por isso ela é a exceção
    /// aceita à regra "nada que seja fonte da verdade vai para o Redis" (ARQUITETURA §6.1).
    /// </summary>
    public sealed record DadosSessao(
        int UsuarioId,
        string Papel,
        DateTimeOffset CriadaEm,
        DateTimeOffset ExpiraEm,
        DateTimeOffset UltimaAtividadeEm,
        bool ManterConectado,
        string UserAgent,
        string Ip)
    {
        public const int TamanhoMaximoUserAgent = 200;

        public bool EhAdmin => Papel == "Admin";
    }

    /// <summary>Marca deixada por um refresh token já rotacionado (<c>refresh-usado:{token}</c>).</summary>
    public sealed record RefreshUsado(string Sid, DateTimeOffset RotacionadoEm);

    /// <summary>
    /// Onde vivem as sessões. Duas implementações, como o <c>IEstadoEfemero</c>:
    /// <see cref="SessaoStoreRedis"/> (produção, compose — compartilhada entre réplicas) e
    /// <see cref="SessaoStoreEmMemoria"/> (testes e execução local sem Redis).
    ///
    /// Falha do armazenamento → <c>ServicoIndisponivelException</c>. Quem chama decide se
    /// isso vira 503 (rotas sensíveis, refresh, login) ou se aceita só o JWT.
    /// </summary>
    public interface ISessaoStore
    {
        /// <summary>
        /// Grava a sessão, o refresh token e a entrada no índice do usuário numa operação só.
        /// </summary>
        Task CriarAsync(string sid, DadosSessao sessao, TimeSpan ttlSessao, string refreshToken, TimeSpan ttlRefresh);

        Task<DadosSessao?> ObterAsync(string sid);

        /// <summary>
        /// Regrava a sessão SÓ se ela ainda existir (<c>SET ... XX</c>): uma sessão revogada
        /// entre a leitura e a escrita nunca ressuscita. Devolve false se não existia.
        /// </summary>
        Task<bool> AtualizarSeExisteAsync(string sid, DadosSessao sessao, TimeSpan ttl);

        /// <summary>Apaga a sessão, a marca de reautenticação e a entrada no índice. True se a sessão existia.</summary>
        Task<bool> RemoverAsync(string sid, int usuarioId);

        /// <summary>Sessões vivas do usuário; membros órfãos do índice são removidos aqui.</summary>
        Task<IReadOnlyList<(string Sid, DadosSessao Sessao)>> ListarDoUsuarioAsync(int usuarioId);

        /// <summary>Sid ao qual o refresh token pertence, ou null se desconhecido/expirado/já consumido.</summary>
        Task<string?> ObterSidDoRefreshAsync(string refreshToken);

        /// <summary>
        /// Consome o refresh token se ele ainda apontar para <paramref name="sid"/> e, na MESMA
        /// operação atômica, grava a marca <c>refresh-usado</c>. Entre N chamadas simultâneas
        /// com o mesmo token, só uma recebe true — e nunca há um instante em que o token já
        /// sumiu e a marca ainda não existe.
        /// </summary>
        Task<bool> ConsumirRefreshAsync(string refreshToken, string sid, RefreshUsado marca, TimeSpan ttlMarca);

        Task GravarRefreshAsync(string refreshToken, string sid, TimeSpan ttl);

        Task<RefreshUsado?> ObterRefreshUsadoAsync(string refreshToken);

        Task MarcarReautenticacaoAsync(string sid, TimeSpan ttl);

        Task<bool> ReautenticouRecentementeAsync(string sid);
    }

    /// <summary>Chaves lógicas (a categoria fica legível; o resto vira HMAC no Redis).</summary>
    internal static class ChavesSessao
    {
        public static string Sessao(string sid) => $"sessao:{sid}";
        public static string Refresh(string token) => $"refresh:{token}";
        public static string RefreshUsado(string token) => $"refresh-usado:{token}";
        public static string Reauth(string sid) => $"reauth:{sid}";
        public static string Indice(int usuarioId) => $"sessoes-usuario:{usuarioId}";
    }
}
