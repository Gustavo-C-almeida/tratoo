using System.Net;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Fonte única de verdade para o IP do cliente.
    ///
    /// Lê <c>HttpContext.Connection.RemoteIpAddress</c> — que, graças ao
    /// <c>UseForwardedHeaders()</c> registrado no início do pipeline
    /// (ver <see cref="ForwardedHeadersSetup"/>), já foi substituído pelo IP
    /// original do cliente extraído do header <c>X-Forwarded-For</c> quando a
    /// aplicação roda atrás de um reverse proxy (Railway, Nginx, Cloudflare…).
    ///
    /// Nenhum endpoint deve ler <c>X-Forwarded-For</c> manualmente: fazer isso
    /// ignora a validação de proxies confiáveis e abre espaço para spoofing.
    /// </summary>
    public static class ClientRequestInfo
    {
        /// <summary>Valor gravado quando o IP não pôde ser determinado.</summary>
        public const string IpDesconhecido = "desconhecido";

        /// <summary>Limite da coluna Ip no banco (IPv6 completo cabe em 45 chars).</summary>
        private const int TamanhoMaximoIp = 45;

        /// <summary>
        /// IP do cliente já normalizado, ou <c>null</c> quando indisponível
        /// (ex.: requisições in-process / TestServer sem conexão real).
        /// </summary>
        public static string? ObterIpOuNulo(HttpContext http)
        {
            var endereco = http.Connection.RemoteIpAddress;
            if (endereco is null)
                return null;

            return Normalizar(endereco);
        }

        /// <summary>
        /// IP do cliente já normalizado, ou <paramref name="padrao"/> quando indisponível.
        /// </summary>
        public static string ObterIp(HttpContext http, string padrao = IpDesconhecido)
            => ObterIpOuNulo(http) ?? padrao;

        /// <summary>
        /// Converte IPv4 mapeado em IPv6 (<c>::ffff:189.1.2.3</c>) para a forma IPv4,
        /// remove o scope id de endereços link-local (<c>fe80::1%12</c>) e trunca no
        /// tamanho máximo persistido. Garante que o mesmo cliente produza sempre a
        /// mesma string — importante para a chave de rate limiting e para a
        /// comparação de registros de auditoria.
        /// </summary>
        public static string Normalizar(IPAddress endereco)
        {
            if (endereco.IsIPv4MappedToIPv6)
                endereco = endereco.MapToIPv4();

            var texto = endereco.ToString();

            var separadorEscopo = texto.IndexOf('%');
            if (separadorEscopo >= 0)
                texto = texto[..separadorEscopo];

            return texto.Length <= TamanhoMaximoIp ? texto : texto[..TamanhoMaximoIp];
        }
    }
}
