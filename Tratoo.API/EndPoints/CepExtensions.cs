namespace Tratoo.API.EndPoints
{
    public static class CepExtensions
    {
        public static void AddEndPointsCep(this WebApplication app)
        {
            app.MapGet("/api/cep/{cep}", async (string cep, IHttpClientFactory httpFactory) =>
            {
                var cepLimpo = cep.Replace("-", "").Trim();
                if (string.IsNullOrWhiteSpace(cepLimpo) || cepLimpo.Length != 8 || !cepLimpo.All(char.IsDigit))
                    return Results.BadRequest(new { mensagem = "CEP inválido. Informe 8 dígitos." });

                try
                {
                    var httpClient = httpFactory.CreateClient("viacep");
                    var response = await httpClient.GetAsync($"https://viacep.com.br/ws/{cepLimpo}/json/");
                    if (!response.IsSuccessStatusCode)
                        return Results.NotFound(new { mensagem = "CEP não encontrado." });

                    var content = await response.Content.ReadAsStringAsync();
                    if (content.Contains("\"erro\""))
                        return Results.NotFound(new { mensagem = "CEP não encontrado." });

                    return Results.Content(content, "application/json");
                }
                catch (HttpRequestException)
                {
                    return Results.StatusCode(503);
                }
            })
            .AllowAnonymous();
        }
    }
}
