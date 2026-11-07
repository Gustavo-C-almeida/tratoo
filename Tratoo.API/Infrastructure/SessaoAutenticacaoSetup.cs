using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Tratoo.Domain.Exceptions;

namespace Tratoo.API.Infrastructure
{
    /// <summary>Marca a rota como sensível: sem conseguir conferir a sessão, responde 503 (fail-closed).</summary>
    public sealed class SessaoVerificadaMetadata
    {
        public static readonly SessaoVerificadaMetadata Instancia = new();
    }

    /// <summary>A requisição não renova a janela de inatividade (ex.: o próprio refresh).</summary>
    public sealed class NaoContaComoAtividadeMetadata
    {
        public static readonly NaoContaComoAtividadeMetadata Instancia = new();
    }

    /// <summary>
    /// Autenticação: JWT curto no cookie <c>tratoo_auth</c> + sessão no servidor
    /// (<see cref="ISessaoStore"/>) conferida a cada requisição em
    /// <c>JwtBearerEvents.OnTokenValidated</c>. Revogar a sessão invalida o token na hora,
    /// sem esperar o "exp".
    ///
    /// Não usa ASP.NET Session, Cookie Authentication nem Antiforgery: os três dependem do
    /// Data Protection, que é local por réplica de propósito (ARQUITETURA §6.5). Nada de
    /// sessão fica na memória do processo — o balanceador não tem afinidade.
    /// </summary>
    public static class SessaoAutenticacaoSetup
    {
        public const string CodigoSessaoEncerrada = "SESSAO_ENCERRADA";
        public const string CodigoSessaoIndisponivel = "SESSAO_INDISPONIVEL";
        public const string CodigoReautenticacao = "REAUTH_REQUIRED";

        public static IServiceCollection AddTratooAutenticacao(this IServiceCollection services, IConfiguration configuration)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(SessaoConfig.DeConfiguracao(configuration));

            // Mesmo critério do estado efêmero: com Redis, as sessões ficam nele (valem em
            // todas as réplicas); sem Redis (testes, dev local de uma réplica), na memória.
            if (EstadoEfemeroSetup.RedisConfigurado(configuration))
                services.AddSingleton<ISessaoStore, SessaoStoreRedis>();
            else
                services.AddSingleton<ISessaoStore, SessaoStoreEmMemoria>();

            services.AddScoped<SessaoService>();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = ctx =>
                        {
                            ctx.Token = ctx.Request.Cookies[SessaoAtual.NomeCookieAccess];
                            return Task.CompletedTask;
                        },
                        OnTokenValidated = ConferirSessaoAsync,
                        OnChallenge = async ctx =>
                        {
                            var codigo = SessaoAtual.CodigoFalha(ctx.HttpContext);
                            if (codigo is null)
                                return;

                            ctx.HandleResponse();
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            await ctx.Response.WriteAsJsonAsync(new
                            {
                                mensagem = "Sua sessão foi encerrada. Entre novamente.",
                                codigoErro = codigo
                            });
                        }
                    };
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"] ?? string.Empty)),
                        ValidateIssuer = true,
                        ValidIssuer = configuration["Jwt:Issuer"],
                        ValidateAudience = true,
                        ValidAudience = configuration["Jwt:Audience"],
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };
                });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Prestador", policy => policy.RequireRole("Prestador"));
                options.AddPolicy("Contratante", policy => policy.RequireRole("Contratante"));
                options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
            });

            return services;
        }

        /// <summary>
        /// Toda requisição com JWT válido: a sessão precisa existir. Sem sid (token anterior a
        /// esta versão), sessão revogada, expirada ou de outro usuário → falha de autenticação
        /// (401 <c>SESSAO_ENCERRADA</c> nas rotas autenticadas).
        /// </summary>
        private static async Task ConferirSessaoAsync(TokenValidatedContext ctx)
        {
            var http = ctx.HttpContext;
            var sid = SessaoAtual.SidDoToken(ctx.Principal);
            var usuarioId = SessaoAtual.UsuarioIdDoToken(ctx.Principal);

            if (sid is null || usuarioId is null)
            {
                SessaoAtual.DefinirFalha(http, CodigoSessaoEncerrada);
                ctx.Fail("Token sem sessão.");
                return;
            }

            var store = http.RequestServices.GetRequiredService<ISessaoStore>();
            var config = http.RequestServices.GetRequiredService<SessaoConfig>();
            var agora = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

            DadosSessao? sessao;
            try
            {
                sessao = await store.ObterAsync(sid);
            }
            catch (ServicoIndisponivelException)
            {
                // Redis fora: o JWT é assinado e vale no máximo 15 min, então as rotas comuns
                // seguem com ele; as sensíveis viram 503 em UseVerificacaoDeSessao. O aviso
                // (no máximo 1 a cada 30 s) já foi registrado pelo store.
                SessaoAtual.DefinirIndisponivel(http, sid);
                return;
            }

            if (sessao is null || sessao.UsuarioId != usuarioId || agora >= sessao.ExpiraEm)
            {
                SessaoAtual.DefinirFalha(http, CodigoSessaoEncerrada);
                ctx.Fail("Sessão encerrada.");
                return;
            }

            var naoContaComoAtividade = SessaoAtual.EhPassiva(http)
                || http.GetEndpoint()?.Metadata.GetMetadata<NaoContaComoAtividadeMetadata>() is not null;

            if (!naoContaComoAtividade)
            {
                try
                {
                    sessao = await SessaoService.RegistrarAtividadeAsync(store, config, sid, sessao, agora);
                }
                catch (ServicoIndisponivelException)
                {
                    // A leitura funcionou e a escrita não: a sessão está conferida; só a
                    // janela de inatividade deixa de ser renovada nesta requisição.
                }
            }

            SessaoAtual.DefinirVerificada(http, sid, sessao);
        }

        /// <summary>
        /// Depois de <c>UseAuthentication</c>: rota sensível com o armazenamento de sessões fora
        /// do ar → 503 + Retry-After: 5. O cliente trata como transitório (não desloga).
        /// </summary>
        public static IApplicationBuilder UseVerificacaoDeSessao(this IApplicationBuilder app) =>
            app.Use(async (http, next) =>
            {
                if (SessaoAtual.Estado(http) == EstadoVerificacaoSessao.ArmazenamentoIndisponivel &&
                    http.GetEndpoint()?.Metadata.GetMetadata<SessaoVerificadaMetadata>() is not null)
                {
                    http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    http.Response.Headers.RetryAfter = TratamentoErrosSetup.SegundosParaNovaTentativaIndisponivel;
                    await http.Response.WriteAsJsonAsync(new
                    {
                        mensagem = "Não foi possível confirmar sua sessão agora. Tente novamente em instantes.",
                        codigoErro = CodigoSessaoIndisponivel
                    });
                    return;
                }

                await next();
            });

        /// <summary>Rota sensível: exige a sessão conferida no armazenamento (fail-closed).</summary>
        public static TBuilder RequireSessaoVerificada<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
            builder.WithMetadata(SessaoVerificadaMetadata.Instancia);

        public static TBuilder NaoContaComoAtividade<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
            builder.WithMetadata(NaoContaComoAtividadeMetadata.Instancia);

        /// <summary>
        /// Exige reautenticação recente (<c>reauth:{sid}</c> existe). Sem ela: 403
        /// <c>REAUTH_REQUIRED</c>, e o frontend abre o modal de senha/MFA e repete a ação.
        /// Implica <see cref="RequireSessaoVerificada{TBuilder}"/>.
        /// </summary>
        public static RouteHandlerBuilder ExigirReautenticacaoRecente(this RouteHandlerBuilder builder) =>
            builder.RequireSessaoVerificada().AddEndpointFilter(async (ctx, next) =>
            {
                var http = ctx.HttpContext;
                var sid = SessaoAtual.Sid(http);
                var store = http.RequestServices.GetRequiredService<ISessaoStore>();

                if (sid is null || SessaoAtual.Estado(http) != EstadoVerificacaoSessao.Verificada ||
                    !await store.ReautenticouRecentementeAsync(sid))
                {
                    return Results.Json(new
                    {
                        mensagem = "Confirme sua identidade para continuar.",
                        codigoErro = CodigoReautenticacao
                    }, statusCode: StatusCodes.Status403Forbidden);
                }

                return await next(ctx);
            });
    }
}
