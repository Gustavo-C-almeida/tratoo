using System.Globalization;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Faz o Kestrel escutar na porta que a plataforma injeta em <c>PORT</c> (Railway,
    /// Heroku, Cloud Run...), mantendo 8080 como fallback.
    ///
    /// O ASP.NET Core não lê <c>PORT</c> sozinho, mas já tem o mecanismo certo para
    /// "escute HTTP nesta porta em todas as interfaces": a configuração
    /// <c>http_ports</c> (a mesma que <c>ASPNETCORE_HTTP_PORTS</c> alimenta). Então não
    /// há lógica de bind própria aqui — só <c>PORT</c> → <c>http_ports</c>.
    ///
    /// Precedência resultante, toda ela do próprio ASP.NET Core:
    ///   1. <c>urls</c> (<c>ASPNETCORE_URLS</c> / <c>--urls</c> / launchSettings) — pedido
    ///      explícito vence. Por isso o Dockerfile NÃO define mais ASPNETCORE_URLS:
    ///      era ele que fazia a aplicação ignorar PORT.
    ///   2. <c>PORT</c> — mapeado aqui para <c>http_ports</c>.
    ///   3. <c>ASPNETCORE_HTTP_PORTS</c> — 8080, fixado no Dockerfile.
    ///
    /// <see cref="ResolverUrlLocal"/> replica essa mesma ordem para a sonda do
    /// HEALTHCHECK, que precisa discar a porta em que o processo realmente está.
    /// </summary>
    public static class PortaHttpSetup
    {
        /// <summary>Variável que a plataforma usa para informar a porta.</summary>
        public const string VariavelPort = "PORT";

        /// <summary>Fallback quando nada informa a porta (mesmo valor do Dockerfile).</summary>
        public const int PortaPadrao = 8080;

        // Chaves de configuração do host web (WebHostDefaults.ServerUrlsKey/HttpPortsKey).
        // ASPNETCORE_URLS e ASPNETCORE_HTTP_PORTS chegam nelas sem o prefixo.
        private const string ChaveUrls      = "urls";
        private const string ChaveHttpPorts = "http_ports";

        /// <summary>
        /// Aplica <c>PORT</c>, se presente, como <c>http_ports</c> do host web.
        /// </summary>
        public static IWebHostBuilder UsarPortaDaPlataforma(
            this IWebHostBuilder webHost, IConfiguration configuration)
        {
            var porta = LerPortaDaPlataforma(configuration);

            if (porta is not null)
                webHost.UseSetting(ChaveHttpPorts, porta.Value.ToString(CultureInfo.InvariantCulture));

            return webHost;
        }

        /// <summary>
        /// <c>null</c> se <c>PORT</c> não foi informada. Valor inválido derruba a subida:
        /// escutar numa porta diferente da que a plataforma roteia deixaria o serviço
        /// fora do ar sem nenhum erro visível — melhor falhar no deploy.
        /// </summary>
        public static int? LerPortaDaPlataforma(IConfiguration configuration)
        {
            var valor = configuration[VariavelPort];

            if (string.IsNullOrWhiteSpace(valor))
                return null;

            if (int.TryParse(valor.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var porta)
                && porta is >= 1 and <= 65535)
                return porta;

            throw new InvalidOperationException(
                $"Variável {VariavelPort} inválida: '{valor}'. Esperado um número de porta entre 1 e 65535.");
        }

        /// <summary>
        /// URL base local em que o processo está servindo, na mesma precedência do
        /// ASP.NET Core (ver resumo da classe). Usada pela sonda do HEALTHCHECK.
        /// </summary>
        public static string ResolverUrlLocal(IConfiguration configuration)
        {
            var urls = configuration[ChaveUrls];

            if (!string.IsNullOrWhiteSpace(urls))
            {
                // Pode vir com várias URLs separadas por ';'. A primeira basta, e o
                // curinga de bind (+ ou *) não é endereço discável — troca por localhost.
                var primeira = urls.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                return primeira.Replace("://+", "://localhost").Replace("://*", "://localhost")
                               .Replace("://[::]", "://localhost").Replace("://0.0.0.0", "://localhost")
                               .TrimEnd('/');
            }

            var porta = LerPortaDaPlataforma(configuration);
            if (porta is not null)
                return $"http://localhost:{porta.Value}";

            var httpPorts = configuration[ChaveHttpPorts];
            if (!string.IsNullOrWhiteSpace(httpPorts))
            {
                var primeira = httpPorts.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                return $"http://localhost:{primeira}";
            }

            return $"http://localhost:{PortaPadrao}";
        }

        /// <summary>
        /// Mesma visão de configuração que o host web monta a partir do ambiente:
        /// variáveis sem prefixo (<c>PORT</c>) + <c>ASPNETCORE_*</c> sem o prefixo
        /// (<c>urls</c>, <c>http_ports</c>). Para quem roda antes do host existir (sonda).
        /// </summary>
        public static IConfiguration ConfiguracaoDoAmbiente() =>
            new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .Build();
    }
}
