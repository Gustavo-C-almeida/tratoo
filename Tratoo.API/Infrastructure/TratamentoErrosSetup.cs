using Tratoo.Domain.Exceptions;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Tradução de exceções em respostas HTTP. Extraído do Program.cs sem mudar o
    /// comportamento existente (400 para regra de negócio, 500 genérico), para o
    /// mapeamento poder ser testado — e com o caso novo:
    ///
    ///  • <see cref="ServicoIndisponivelException"/> → 503 + Retry-After. Dependência fora
    ///    do ar (ex.: Redis dos OTPs): a operação é recusada (fail-closed), e o cliente
    ///    sabe que é temporário, não erro de negócio nem bug.
    /// </summary>
    public static class TratamentoErrosSetup
    {
        public const string SegundosParaNovaTentativaIndisponivel = "5";

        public static IApplicationBuilder UseTratooTratamentoDeErros(this IApplicationBuilder app) =>
            app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
            {
                var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                var ex = feature?.Error;

                ctx.Response.ContentType = "application/json";

                if (ex is NegocioException negocio)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsJsonAsync(new { mensagem = negocio.Message });
                }
                else if (ex is ServicoIndisponivelException indisponivel)
                {
                    // A causa já foi registrada por quem lançou.
                    ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    ctx.Response.Headers.RetryAfter = SegundosParaNovaTentativaIndisponivel;
                    await ctx.Response.WriteAsJsonAsync(new { mensagem = indisponivel.Message });
                }
                else
                {
                    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "Erro não tratado em {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsJsonAsync(new { mensagem = "Erro interno. Tente novamente mais tarde." });
                }
            }));
    }
}
