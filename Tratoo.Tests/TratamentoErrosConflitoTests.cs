using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tratoo.API.Infrastructure;
using Tratoo.Domain.Exceptions;
using Xunit;

namespace Tratoo.Tests
{
    /// <summary>
    /// Disputa concorrente perdida (<see cref="ConflitoException"/>) vira 409 com a mensagem;
    /// regra de negócio comum continua 400.
    /// </summary>
    public class TratamentoErrosConflitoTests
    {
        [Fact]
        public async Task Conflito_vira_409_com_mensagem_e_regra_de_negocio_continua_400()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            app.UseTratooTratamentoDeErros();
            app.MapGet("/conflito", string () => throw new ConflitoException("O pagamento está sendo processado por outra operação."));
            app.MapGet("/negocio", string () => throw new NegocioException("Regra violada."));
            await app.StartAsync();
            var cliente = app.GetTestClient();

            var conflito = await cliente.GetAsync("/conflito");
            Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
            Assert.Equal("O pagamento está sendo processado por outra operação.",
                (await conflito.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["mensagem"]);

            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync("/negocio")).StatusCode);
        }
    }
}
