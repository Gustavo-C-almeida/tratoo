namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Modo sonda: <c>dotnet Tratoo.API.dll --healthcheck</c> faz um GET em
    /// <c>/health/live</c> no próprio processo que está servindo e encerra com
    /// código 0 (saudável) ou 1 (não saudável).
    ///
    /// POR QUE ISTO EXISTE: a imagem <c>mcr.microsoft.com/dotnet/aspnet:8.0</c> não
    /// traz curl nem wget, e instalá-los só para o HEALTHCHECK adicionaria pacotes
    /// e superfície de ataque à imagem de runtime. O binário da aplicação já está
    /// lá e já tem HttpClient — usá-lo custa zero dependência.
    ///
    /// Não é usado pela Railway, que faz a própria sonda HTTP externa via
    /// healthcheckPath (ver railway.toml). É para o HEALTHCHECK do Docker e para
    /// qualquer orquestrador que use a imagem.
    /// </summary>
    public static class SondaHealthCheck
    {
        public const string Argumento = "--healthcheck";

        /// <summary>Margem curta: a sonda do Docker não deve segurar o contêiner.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        public static bool FoiSolicitada(string[] args) =>
            args.Any(a => string.Equals(a, Argumento, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Retorna o exit code: 0 se <c>/health/live</c> respondeu 2xx, 1 caso contrário.
        /// </summary>
        public static async Task<int> ExecutarAsync()
        {
            var url = $"{ResolverBaseUrl()}{HealthCheckSetup.RotaLive}";

            try
            {
                using var client = new HttpClient { Timeout = Timeout };
                using var resposta = await client.GetAsync(url);

                if (resposta.IsSuccessStatusCode)
                    return 0;

                await Console.Error.WriteLineAsync(
                    $"[healthcheck] {url} respondeu {(int)resposta.StatusCode}.");
                return 1;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[healthcheck] {url} falhou: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Descobre em que porta o processo está servindo, na mesma ordem de
        /// precedência que o ASP.NET Core usa, para a sonda não ficar presa a um
        /// 8080 hard-coded se a porta mudar (ex.: PORT injetado pela plataforma).
        /// </summary>
        private static string ResolverBaseUrl()
        {
            var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

            if (!string.IsNullOrWhiteSpace(urls))
            {
                // Pode vir com várias URLs separadas por ';'. A primeira basta, e o
                // curinga de bind (+ ou *) não é endereço discável — troca por localhost.
                var primeira = urls.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                return primeira.Replace("://+", "://localhost").Replace("://*", "://localhost")
                               .TrimEnd('/');
            }

            var port = Environment.GetEnvironmentVariable("PORT");
            return string.IsNullOrWhiteSpace(port)
                ? "http://localhost:8080"
                : $"http://localhost:{port}";
        }
    }
}
