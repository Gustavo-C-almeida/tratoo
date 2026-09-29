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
        public static Task<int> ExecutarAsync() =>
            ExecutarAsync(PortaHttpSetup.ConfiguracaoDoAmbiente());

        /// <summary>
        /// Descobre a porta pela MESMA resolução que o host web usa
        /// (<see cref="PortaHttpSetup.ResolverUrlLocal"/>) — assim a sonda não fica presa
        /// a um 8080 hard-coded quando a plataforma injeta outro PORT.
        /// </summary>
        public static async Task<int> ExecutarAsync(IConfiguration configuration)
        {
            string url;

            try
            {
                url = $"{PortaHttpSetup.ResolverUrlLocal(configuration)}{HealthCheckSetup.RotaLive}";
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[healthcheck] configuração de porta inválida: {ex.Message}");
                return 1;
            }

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
    }
}
