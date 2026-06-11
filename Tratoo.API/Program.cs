using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Serilog;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Tratoo.Domain.Data;
using Tratoo.Domain.Exceptions;
using Tratoo.API.BackgroundServices;
using Tratoo.API.EndPoints;
using Microsoft.Extensions.FileProviders;
using Amazon.S3;
using Amazon.S3.Model;
using System;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ─── Configurar Serilog para logs em arquivo e console ──────────────────────
builder.Host.UseSerilog((ctx, config) =>
    config
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/openai-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}{Exception}",
            retainedFileCountLimit: 30));

builder.Services.AddDbContext<TratooContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

builder.Services.AddDbContext<VectorContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("VectorConnection"),
        o => o.UseVector()
    )
);

builder.Services.AddScoped<VectorDbInitializer>();

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddMemoryCache();

// Repositories
builder.Services.AddScoped<IPrestadorRepository, PrestadorRepository>();
builder.Services.AddScoped<IContratanteRepository, ContratanteRepository>();
builder.Services.AddScoped<IContaBancariaRepository, ContaBancariaRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IIdentidadeRepository, IdentidadeRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IConsentLogRepository, ConsentLogRepository>();
builder.Services.AddScoped<IPerfilProfissaoPrestadorRepository, PerfilProfissaoPrestadorRepository>();
builder.Services.AddScoped<IProjetoRepository, ProjetoRepository>();
builder.Services.AddScoped<IPropostaProjetoRepository, PropostaProjetoRepository>();
builder.Services.AddScoped<IMensagemProjetoRepository, MensagemProjetoRepository>();
builder.Services.AddScoped<IContratoServicoRepository, ContratoServicoRepository>();
builder.Services.AddScoped<IEntregaRepository, EntregaRepository>();
builder.Services.AddScoped<IPagamentoRepository, PagamentoRepository>();
builder.Services.AddScoped<IAvaliacaoRepository, AvaliacaoRepository>();
builder.Services.AddScoped<IConviteProjetoRepository, ConviteProjetoRepository>();

// Services — domínio
builder.Services.AddScoped<CompetenciaService>();
builder.Services.AddScoped<ExperienciaService>();
builder.Services.AddScoped<CertificacaoService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<PerfilProfissaoPrestadorService>();
builder.Services.AddScoped<IDadosBancariosService, DadosBancariosService>();
builder.Services.AddScoped<MyOwnProfilePrestadorService>();
builder.Services.AddScoped<CompetenciaRelacionamentoService>();
builder.Services.AddScoped<ICadastroService, CadastroService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<IIdentidadeService, IdentidadeService>();
builder.Services.AddScoped<IExclusaoContaService, ExclusaoContaService>();
builder.Services.Configure<Tratoo.Domain.Config.EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IVerificacaoMFAService, VerificacaoMFAService>();
builder.Services.AddScoped<ICacheTempService, CacheTempService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<PerfilContratanteService>();
builder.Services.AddScoped<ProjetoService>();
builder.Services.AddScoped<IContratoServicoService, ContratoServicoService>();
builder.Services.AddScoped<IEntregaService, EntregaService>();
builder.Services.AddScoped<PropostaProjetoService>();
builder.Services.AddScoped<MensagemProjetoService>();
builder.Services.AddScoped<ConviteProjetoService>();
builder.Services.AddScoped<IAvaliacaoService, AvaliacaoService>();
builder.Services.AddScoped<IPagamentoService, PagamentoService>();
builder.Services.AddScoped<AdminDisputaService>();
builder.Services.AddHttpClient<IdentidadeService>();  // HttpClient para BrasilAPI
builder.Services.AddHttpClient("viacep");             // HttpClient para proxy ViaCEP

// Gateway de pagamento — Asaas
builder.Services.Configure<AsaasConfig>(builder.Configuration.GetSection("Asaas"));
builder.Services.AddHttpClient<IAsaasGatewayService, AsaasGatewayService>();

// ─── Busca Semântica com IA ───────────────────────────────────────────────────
// HTTP client para OpenAI Embeddings API (text-embedding-3-small, 1536 dims)
builder.Services.AddScoped<OpenAiLoggingHandler>();
builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OpenAI:BaseUrl"]!);
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", builder.Configuration["OpenAI:ApiKey"]);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<OpenAiLoggingHandler>();

builder.Services.AddScoped<TextoNormalizadorService>();
builder.Services.AddScoped<PrestadorIndexadorService>();
builder.Services.AddScoped<ProjetoIndexadorService>();
builder.Services.AddScoped<BuscaSemanticaService>();
builder.Services.AddScoped<ReindexacaoBackgroundService.IBatchReindexador,
                            ReindexacaoBackgroundService.BatchReindexador>();

// Background services
builder.Services.AddHostedService<PropostaExpiracaoService>();
builder.Services.AddHostedService<ContratoExpiracaoService>();
builder.Services.AddHostedService<PagamentoLiberacaoService>();
builder.Services.AddHostedService<AvaliacaoExpiracaoService>();
builder.Services.AddHostedService<ReindexacaoBackgroundService>();

// Cloudflare R2 — bucket público (fotos de perfil, portfólio)
builder.Services.Configure<R2Config>(builder.Configuration.GetSection("CloudflareR2"));
builder.Services.AddScoped<IArquivoStorageService, R2StorageService>();

// Cloudflare R2 — bucket privado (PDFs de contratos)
builder.Services.Configure<R2PrivateConfig>(builder.Configuration.GetSection("CloudflareR2Private"));
builder.Services.AddScoped<IR2PrivateStorageService, R2PrivateStorageService>();
builder.Services.AddScoped<IContratosPdfService, ContratosPdfService>();

// Autenticação JWT via cookie httpOnly
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                ctx.Token = ctx.Request.Cookies["tratoo_auth"];
                return Task.CompletedTask;
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]!)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Prestador", policy => policy.RequireRole("Prestador"));
    options.AddPolicy("Contratante", policy => policy.RequireRole("Contratante"));
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});

builder.Services.AddRateLimiter(options =>
{
    // Máximo 5 tentativas por minuto por IP nos endpoints de cadastro
    options.AddFixedWindowLimiter("cadastro", cfg =>
    {
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.PermitLimit = 5;
        cfg.QueueLimit = 0;
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Máximo 10 tentativas por minuto por IP no login (brute-force protection)
    options.AddFixedWindowLimiter("login", cfg =>
    {
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.PermitLimit = 10;
        cfg.QueueLimit = 0;
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Máximo 3 solicitações por minuto por IP na redefinição de senha
    options.AddFixedWindowLimiter("senha", cfg =>
    {
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.PermitLimit = 3;
        cfg.QueueLimit = 0;
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Máximo 5 requisições por minuto por IP no fluxo de dados bancários (token/confirmar/salvar)
    options.AddFixedWindowLimiter("dados-bancarios", cfg =>
    {
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.PermitLimit = 5;
        cfg.QueueLimit = 0;
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Máximo 3 solicitações de OTP por minuto (protege contra spam de e-mail)
    options.AddFixedWindowLimiter("otp-assinatura", cfg =>
    {
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.PermitLimit = 3;
        cfg.QueueLimit = 0;
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { mensagem = "Muitas tentativas. Aguarde antes de tentar novamente." },
            cancellationToken);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Informe o token JWT. Exemplo: Bearer {seu_token}"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Inicializa PostgreSQL + índices HNSW na subida da aplicação
using (var scope = app.Services.CreateScope())
{
    var vectorInit = scope.ServiceProvider.GetRequiredService<VectorDbInitializer>();
    await vectorInit.InitializeAsync();
}

// Cabeçalhos de segurança aplicados a todas as respostas (estáticas e de API).
// CSP permite 'unsafe-inline' porque o frontend usa estilos e handlers inline;
// endurecer para nonces é uma melhoria futura sem impacto funcional imediato.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";

    // Em desenvolvimento, libera Browser Link (VS) e hot reload do ASP.NET
    var connectExtra = app.Environment.IsDevelopment()
        ? "http://localhost:* ws://localhost:* wss://localhost:* "
        : "";

    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        // Font Awesome (cdnjs) — folha de estilo e webfonts
        "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
        "font-src 'self' https://cdnjs.cloudflare.com; " +
        "img-src 'self' data: https:; " +
        $"connect-src 'self' {connectExtra}; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";
    await next();
});

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
    else
    {
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Erro não tratado em {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { mensagem = "Erro interno. Tente novamente mais tarde." });
    }
}));

var caminhoWeb = Path.Combine(
    Directory.GetCurrentDirectory(),
    "..",
    "Tratoo.Web",
    "wwwroot"
);

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = new PhysicalFileProvider(caminhoWeb)
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(caminhoWeb),
    RequestPath = ""
});

Console.WriteLine(caminhoWeb);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// ─── Guarda de onboarding ─────────────────────────────────────────────────────
// Bloqueia chamadas de API de usuários autenticados sem onboarding completo.
// Arquivos estáticos nunca chegam aqui (short-circuitados por UseStaticFiles antes).
//
// ATENÇÃO: o cookie JWT é enviado em TODA requisição same-origin, inclusive em
// rotas públicas como /usuarios/login e /usuarios/cadastro. Por isso essas rotas
// também devem ser explicitamente isentas — mesmo que não exijam autenticação,
// o middleware pode ver IsAuthenticated=true se o cookie ainda estiver presente.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true &&
        context.User.FindFirst("perfilCompleto")?.Value != "true")
    {
        var path = context.Request.Path.Value ?? string.Empty;

        var isIsenta =
            // Endpoint de dados do usuário atual (consultado pelo guard do frontend)
            path.Equals("/api/me", StringComparison.OrdinalIgnoreCase) ||
            // Fluxo de onboarding
            path.StartsWith("/usuarios/onboarding", StringComparison.OrdinalIgnoreCase) ||
            // Logout (deve sempre funcionar)
            path.StartsWith("/usuarios/logout", StringComparison.OrdinalIgnoreCase) ||
            // Login e MFA — o cookie pode estar presente mas o usuário quer trocar de conta
            // ou o token expirou e ele precisa se reautenticar
            path.StartsWith("/usuarios/login", StringComparison.OrdinalIgnoreCase) ||
            // Cadastro e confirmação de e-mail — fluxo público, cookie não deve bloquear
            path.StartsWith("/usuarios/cadastro", StringComparison.OrdinalIgnoreCase) ||
            // Redefinição de senha — fluxo público, usuário pode ter cookie expirado/inválido
            path.StartsWith("/usuarios/senha/resetar", StringComparison.OrdinalIgnoreCase) ||
            // Swagger (dev/teste)
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            // CEP — usado no próprio onboarding para buscar endereço
            path.StartsWith("/api/cep/", StringComparison.OrdinalIgnoreCase);

        if (!isIsenta)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new
            {
                mensagem = "Complete seu perfil antes de acessar esta funcionalidade.",
                codigoErro = "ONBOARDING_PENDENTE"
            });
            return;
        }
    }

    await next();
});

// Endpoint público: informações de configuração financeira para exibição no frontend
// (taxa operacional estimada do gateway — apenas informativo, não participa de cálculos)
app.MapGet("/api/config/financeiro", (HttpContext http) =>
{
    var cfg = http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AsaasConfig>>().Value;
    return Results.Ok(new
    {
        taxaOperacionalEstimadaPercentual = cfg.TaxaOperacionalEstimadaPercentual,
        taxaOperacionalEstimadaDisplay    = $"{cfg.TaxaOperacionalEstimadaPercentual * 100:F2}%",
        observacao = "Taxa cobrada pela operadora de pagamentos (Asaas). Não é uma taxa da plataforma Tratoo. Valores estimados — podem variar conforme a transação."
    });
});

app.AddEndPointsCep();
app.AddEndPointsCompetencias();
app.AddEndPointsUsers();
app.AddEndPointsPerfil();
app.AddEndPointsDadosBancarios();
app.AddEndPointsContratante();
app.AddEndPointsCompetenciaRelacionamento();
app.AddEndPointsProjeto();
app.AddEndPointsProposta();
app.AddEndPointsContrato();
app.AddEndPointsPagamento();
app.AddEndPointsAvaliacao();
app.AddEndPointsConviteProjeto();
app.AddEndPointsChatConvite();
app.AddEndPointsBusca();
app.AddEndPointsAdminDisputa();
app.AddEndPointsDevSeed();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();
