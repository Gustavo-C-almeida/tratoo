using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;
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
    /// Aqui a partição é explícita, via <c>AddPolicy</c> + <c>RateLimitPartition</c>,
    /// usando o IP do cliente. O IP vem de <see cref="ClientRequestInfo"/>, portanto
    /// já é o IP real devolvido pelo middleware de Forwarded Headers.
    ///
    /// Cada política registrada por <c>AddPolicy</c> tem seu próprio espaço de
    /// partições — duas políticas distintas NÃO compartilham balde mesmo quando
    /// devolvem a mesma chave. Verificado por teste
    /// (<c>PoliticasComChaveCruaIdentica_AindaAssimNaoCompartilhamBalde</c>). O nome
    /// da política ainda entra na chave para facilitar diagnóstico, não por
    /// necessidade de isolamento.
    /// </summary>
    public static class RateLimiterSetup
    {
        public const string PoliticaCadastro        = "cadastro";
        public const string PoliticaLogin           = "login";
        public const string PoliticaSenha           = "senha";
        public const string PoliticaDadosBancarios  = "dados-bancarios";
        public const string PoliticaOtpAssinatura   = "otp-assinatura";

        /// <summary>
        /// Valor do header <c>Retry-After</c> nas respostas 429, em segundos.
        /// Igual à janela das políticas (1 minuto).
        /// </summary>
        public const string SegundosParaNovaTentativa = "60";

        /// <summary>Janela de todas as políticas.</summary>
        private static readonly TimeSpan Janela = TimeSpan.FromMinutes(1);

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
                    // Retry-After precisa ser escrito ANTES do corpo: WriteAsJsonAsync
                    // inicia a resposta e a partir daí os headers ficam imutáveis.
                    //
                    // 60 = o tamanho da janela. É um limite superior seguro: numa janela
                    // fixa o reset acontece em no máximo 60 s, então o cliente nunca é
                    // orientado a voltar cedo demais. Sem este header, um cliente que
                    // tome 429 tende a re-tentar em loop imediato.
                    context.HttpContext.Response.Headers.RetryAfter = SegundosParaNovaTentativa;
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
            var opcoes = new FixedWindowRateLimiterOptions
            {
                Window = Janela,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            };

            options.AddPolicy(nomePolitica, http =>
            {
                var ip = ClientRequestInfo.ObterIp(http);

                // O nome da política entra na chave só para tornar a partição
                // legível em diagnóstico — o isolamento entre políticas já é
                // garantido pelo próprio AddPolicy.
                var particao = $"{nomePolitica}|{ip}";

                // Com Redis (várias réplicas): contador único para a aplicação inteira,
                // degradando para memória se o Redis cair (LimitadorJanelaFixaRedis).
                var redis = http.RequestServices.GetService<IConnectionMultiplexer>();
                if (redis is not null)
                {
                    var protecao = http.RequestServices.GetRequiredService<ProtecaoEstadoEfemero>();
                    var logger = http.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(LimitadorJanelaFixaRedis));

                    // O IP é dado pessoal: no Redis a chave vai como HMAC (ProtecaoEstadoEfemero).
                    return RateLimitPartition.Get(particao,
                        _ => new LimitadorJanelaFixaRedis(redis, protecao.NomeDaChave($"rl:{particao}"), opcoes, logger));
                }

                // Sem Redis (1 réplica, produção hoje): exatamente o comportamento anterior.
                // GetFixedWindowLimiter força AutoReplenishment=false e delega a
                // reposição ao timer do PartitionedRateLimiter da política.
                return RateLimitPartition.GetFixedWindowLimiter(particao, _ => opcoes);
            });
        }
    }
}
