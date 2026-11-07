using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Tratoo.API.Infrastructure
{
    /// <summary>Como a sessão foi conferida nesta requisição.</summary>
    public enum EstadoVerificacaoSessao
    {
        /// <summary>Sem token válido (anônimo) — nada foi conferido.</summary>
        NaoAutenticada,
        /// <summary><c>sessao:{sid}</c> existe e pertence ao usuário do token.</summary>
        Verificada,
        /// <summary>
        /// O armazenamento de sessões não respondeu. Rotas sensíveis respondem 503; as demais
        /// aceitam o JWT (assinado, no máximo 15 min de validade).
        /// </summary>
        ArmazenamentoIndisponivel
    }

    /// <summary>O que a validação do token deixou no <see cref="HttpContext"/> para o resto do pipeline.</summary>
    public static class SessaoAtual
    {
        public const string NomeCookieAccess = "tratoo_auth";
        public const string NomeCookieRefresh = "tratoo_refresh";
        public const string CaminhoCookieRefresh = "/api/auth";
        public const string HeaderPassivo = "X-Tratoo-Passivo";

        private const string ChaveSid = "tratoo.sessao.sid";
        private const string ChaveDados = "tratoo.sessao.dados";
        private const string ChaveEstado = "tratoo.sessao.estado";
        private const string ChaveCodigoFalha = "tratoo.sessao.codigo-falha";

        /// <summary>
        /// Sid do token. O JwtBearer mapeia "sid" para <see cref="ClaimTypes.Sid"/> na entrada,
        /// então os dois nomes são aceitos.
        /// </summary>
        public static string? SidDoToken(ClaimsPrincipal? usuario) =>
            usuario?.FindFirst(JwtRegisteredClaimNames.Sid)?.Value ?? usuario?.FindFirst(ClaimTypes.Sid)?.Value;

        public static int? UsuarioIdDoToken(ClaimsPrincipal? usuario)
        {
            var valor = usuario?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? usuario?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return int.TryParse(valor, out var id) ? id : null;
        }

        public static string? Sid(HttpContext http) => http.Items[ChaveSid] as string;
        public static DadosSessao? Dados(HttpContext http) => http.Items[ChaveDados] as DadosSessao;

        public static EstadoVerificacaoSessao Estado(HttpContext http) =>
            http.Items[ChaveEstado] is EstadoVerificacaoSessao e ? e : EstadoVerificacaoSessao.NaoAutenticada;

        /// <summary>Código devolvido no 401 quando o token era válido mas a sessão não (ex.: SESSAO_ENCERRADA).</summary>
        public static string? CodigoFalha(HttpContext http) => http.Items[ChaveCodigoFalha] as string;

        /// <summary>
        /// Requisição de fundo (polling do chat, notificações): não conta como atividade. O
        /// header só reduz privilégio — no pior caso, um cliente que mente perde a sessão por
        /// inatividade —, então é seguro confiar nele.
        /// </summary>
        public static bool EhPassiva(HttpContext http) => http.Request.Headers[HeaderPassivo] == "1";

        internal static void DefinirVerificada(HttpContext http, string sid, DadosSessao dados)
        {
            http.Items[ChaveSid] = sid;
            http.Items[ChaveDados] = dados;
            http.Items[ChaveEstado] = EstadoVerificacaoSessao.Verificada;
        }

        internal static void DefinirIndisponivel(HttpContext http, string sid)
        {
            http.Items[ChaveSid] = sid;
            http.Items[ChaveEstado] = EstadoVerificacaoSessao.ArmazenamentoIndisponivel;
        }

        internal static void DefinirFalha(HttpContext http, string codigo) => http.Items[ChaveCodigoFalha] = codigo;

        /// <summary>Quando a sessão expira por inatividade e o teto absoluto.</summary>
        public static (DateTimeOffset PorInatividade, DateTimeOffset Absoluta) Expiracoes(DadosSessao s, PoliticaSessao p)
        {
            var porInatividade = s.UltimaAtividadeEm + p.Inatividade;
            return (porInatividade < s.ExpiraEm ? porInatividade : s.ExpiraEm, s.ExpiraEm);
        }

        /// <summary>TTL da chave da sessão: o menor entre a janela de inatividade e o tempo até a expiração absoluta.</summary>
        public static TimeSpan TtlSessao(DadosSessao s, PoliticaSessao p, DateTimeOffset agora)
        {
            var ateAbsoluta = s.ExpiraEm - agora;
            return p.Inatividade < ateAbsoluta ? p.Inatividade : ateAbsoluta;
        }
    }
}
