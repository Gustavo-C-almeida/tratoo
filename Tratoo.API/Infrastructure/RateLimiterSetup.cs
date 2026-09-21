using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Tratoo.API.Infrastructure
{
    /// <summary>
    /// Políticas de rate limiting particionadas por IP do cliente.
    ///
    /// ATENÇÃO — armadilha corrigida aqui: <c>RateLimiterOptions.AddFixedWindowLimiter</c>
    /// NÃO particiona nada. Ele cria a partição com uma chave constante
    /// (<c>PolicyNameKey</c>), ou seja, um único balde compartilhado por TODOS os
    /// clientes. Com 5 cadastros/minuto, a plataforma inteira ficava limitada a
    /// 5 cadastros por minuto — e um visitante qualquer conseguia bloquear o
    /// login de todo mundo com 10 requisições.
    ///
    /// Aqui a partição é explícita: "{política}|{ip}". O nome da política entra na
    /// chave porque o middleware mantém um único <c>PartitionedRateLimiter</c>
    /// compartilhado entre todas as políticas — chaves iguais em políticas
    /// diferentes cairiam no mesmo balde.
    ///
    /// O IP vem de <see cref="ClientRequestInfo"/>, portanto já é o IP real do
    /// cliente devolvido pelo middleware de Forwarded Headers.
    /// </summary>
    public static class RateLimiterSetup
    {
        public const string PoliticaCadastro        = "cadastro";
        public const string PoliticaLogin           = "login";
        public const string PoliticaSenha           = "senha";
        public const string PoliticaDadosBancarios  = "dados-bancarios";
        public const string PoliticaOtpAssinatura   = "otp-assinatura";

        public static IServiceCollection AddTratooRateLimiter(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                // Máximo 5 tentativas por minuto por IP nos endpoints de cadastro
                options.AdicionarPoliticaPorIp(PoliticaCadastro, permitLimit: 5);

                // Máximo 10 tentativas por minuto por IP no login (brute-force protection)
                options.AdicionarPoliticaPorIp(PoliticaLogin, permitLimit: 10);

                // Máximo 3 solicitações por minuto por IP na redefinição de senha
                options.AdicionarPoliticaPorIp(PoliticaSenha, permitLimit: 3);

                // Máximo 5 requisições por minuto por IP no fluxo de dados bancários
                // (token/confirmar/salvar)
                options.AdicionarPoliticaPorIp(PoliticaDadosBancarios, permitLimit: 5);

                // Máximo 3 solicitações de OTP por minuto por IP (protege contra spam de e-mail)
                options.AdicionarPoliticaPorIp(PoliticaOtpAssinatura, permitLimit: 3);

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { mensagem = "Muitas tentativas. Aguarde antes de tentar novamente." },
                        cancellationToken);
                };
            });

            return services;
        }

        /// <summary>
        /// Janela fixa de 1 minuto, particionada por IP do cliente.
        /// Requisições sem IP identificável caem numa partição compartilhada
        /// ("desconhecido") — conservador por definição: preferimos limitar demais
        /// a liberar um atacante que conseguiu esconder a origem.
        /// </summary>
        private static void AdicionarPoliticaPorIp(
            this RateLimiterOptions options, string nomePolitica, int permitLimit)
        {
            options.AddPolicy(nomePolitica, http =>
            {
                var ip = ClientRequestInfo.ObterIp(http);

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{nomePolitica}|{ip}",
                    // GetFixedWindowLimiter força AutoReplenishment=false e delega a
                    // reposição ao timer único do PartitionedRateLimiter do middleware.
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = permitLimit,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });
        }
    }
}
