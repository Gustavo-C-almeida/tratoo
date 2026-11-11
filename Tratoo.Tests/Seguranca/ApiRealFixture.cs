using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Features.Shared;
using Tratoo.Domain.Models;
using Tratoo.Domain.Models.Financeiro;
using Tratoo.Tests.Integracao;
using Xunit;

namespace Tratoo.Tests.Seguranca
{
    /// <summary>
    /// Sobe a API DE VERDADE (<c>dotnet Tratoo.API.dll</c>, processo separado, ambiente
    /// Production) sobre Postgres + Redis reais, com todos os serviços externos apontando para
    /// <see cref="ServidorExternoFalso"/>. Os testes de segurança falam HTTP com ela como um
    /// cliente qualquer: nada de pipeline montado à mão, o que se testa é o Program.cs real.
    ///
    /// Infraestrutura, em ordem de preferência:
    ///  • <c>TRATOO_SEGURANCA_PG</c> (connection string de servidor, com usuário que cria bancos)
    ///    e <c>TRATOO_SEGURANCA_REDIS</c> (connection string do StackExchange.Redis): serviços
    ///    já em execução — útil fora do Docker;
    ///  • senão, containers Docker (<c>pgvector/pgvector:pg16</c> e <c>redis:7-alpine</c>), como
    ///    os demais testes de integração e o CI.
    ///
    /// Três processos: <see cref="Api"/> e <see cref="Replica"/> (Production, mesmo banco e mesmo
    /// Redis — duas réplicas atrás do balanceador) e <see cref="ApiDev"/> (Development, só para o
    /// inventário de rotas via Swagger).
    /// </summary>
    public sealed class ApiRealFixture : IAsyncLifetime
    {
        public const string Senha = "Teste@Seguranca2026";
        public const string Emissor = "tratoo-testes-seguranca";
        public const string WebhookToken = "token-webhook-dos-testes-de-seguranca-0123";

        /// <summary>Chave do JWT da API sob teste. Conhecida aqui só para FORJAR tokens inválidos.</summary>
        public static readonly string ChaveJwt = "chave-jwt-dos-testes-de-seguranca-" + new string('x', 32);

        private ContainerDocker? _postgres;
        private ContainerDocker? _redis;
        private readonly List<ProcessoApiReal> _processos = new();
        private static int _contadorIp;

        public ServidorExternoFalso Externo { get; private set; } = null!;
        public string Conexao { get; private set; } = "";
        public string ConexaoRedis { get; private set; } = "";
        public ProcessoApiReal Api { get; private set; } = null!;
        public ProcessoApiReal Replica { get; private set; } = null!;
        public ProcessoApiReal ApiDev { get; private set; } = null!;
        public Massa Dados { get; private set; } = null!;

        public static bool Disponivel =>
            (Environment.GetEnvironmentVariable("TRATOO_SEGURANCA_PG") is { Length: > 0 }
             && Environment.GetEnvironmentVariable("TRATOO_SEGURANCA_REDIS") is { Length: > 0 })
            || DockerCli.Disponivel;

        /// <summary>IP de cliente inédito por chamada: isola os baldes de rate limit entre testes.</summary>
        public static string NovoIp()
        {
            var n = Interlocked.Increment(ref _contadorIp);
            return $"198.18.{n / 250 % 250}.{n % 250 + 1}";
        }

        public async Task InitializeAsync()
        {
            if (!Disponivel)
            {
                DockerCli.FalharSeExigido();
                return;
            }

            Externo = await ServidorExternoFalso.IniciarAsync();
            var servidorPg = await PrepararPostgresAsync();
            ConexaoRedis = await PrepararRedisAsync();

            var sufixo = Guid.NewGuid().ToString("N")[..10];
            Conexao = await CriarBancoAsync(servidorPg, $"seg_{sufixo}");
            var conexaoVetor = await CriarBancoAsync(servidorPg, $"seg_{sufixo}_vetor");

            // Em produção a extensão já existe quando a API sobe (o pre-deploy roda --migrate-only).
            // Num banco recém-criado, o Npgsql da API carregaria os tipos antes de o
            // VectorDbInitializer criar a extensão e não reconheceria "vector" (500 na busca com q).
            await using (var c = new NpgsqlConnection(conexaoVetor))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", c);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var db = SeedIntegracao.Contexto(Conexao))
                await db.Database.MigrateAsync();

            Dados = await Massa.CriarAsync(Conexao);

            var ambiente = Ambiente(conexaoVetor);
            Api = await IniciarAsync("api", ambiente, "Production");
            Replica = await IniciarAsync("replica", ambiente, "Production");
            ApiDev = await IniciarAsync("dev", ambiente, "Development");
        }

        private Dictionary<string, string?> Ambiente(string conexaoVetor) => new()
        {
            ["ConnectionStrings__DefaultConnection"] = Conexao,
            ["ConnectionStrings__VectorConnection"] = conexaoVetor,
            ["Jwt__SecretKey"] = ChaveJwt,
            ["Jwt__Issuer"] = Emissor,
            ["Jwt__Audience"] = Emissor,
            ["DataProtector__Chave"] = ConfiguracaoDosTestes.ChaveDeDadosDeTeste,
            ["Redis__ConnectionString"] = ConexaoRedis,
            // Aleatória por execução: as chaves no Redis (HMAC) não colidem com as de uma execução
            // anterior — sem isso, um Redis reaproveitado herdaria baldes de rate limit.
            ["Redis__ChaveProtecao"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            ["Asaas__BaseUrl"] = $"{Externo.BaseUrl}/asaas/api/v3",
            ["Asaas__ApiKey"] = "chave-asaas-falsa",
            ["Asaas__WebhookToken"] = WebhookToken,
            ["Resend__ApiKey"] = "re_falsa",
            ["Resend__FromEmail"] = "testes@tratoo.invalid",
            ["Resend__BaseUrl"] = $"{Externo.BaseUrl}/resend/",
            ["OpenAI__BaseUrl"] = $"{Externo.BaseUrl}/openai/v1/",
            ["OpenAI__ApiKey"] = "sk-falsa",
            ["CloudflareR2Private__AccountId"] = "falso",
            ["CloudflareR2Private__AccessKey"] = "falso",
            ["CloudflareR2Private__SecretKey"] = "falso",
            ["CloudflareR2Private__BucketName"] = "privado",
            ["CloudflareR2Private__ServiceURL"] = Externo.BaseUrl,
            ["LogArquivo__Habilitado"] = "false",
            // Background services não devem mexer na massa durante os testes.
            ["DataProtector__RecifrarAutomaticamente"] = "false",
        };

        private async Task<string> PrepararPostgresAsync()
        {
            var externo = Environment.GetEnvironmentVariable("TRATOO_SEGURANCA_PG");
            if (!string.IsNullOrWhiteSpace(externo))
                return externo;

            const string senha = "teste_seguranca";
            _postgres = ContainerDocker.Iniciar("pgvector/pgvector:pg16", 5432, ambiente: new[] { $"POSTGRES_PASSWORD={senha}" });
            var servidor = $"Host=127.0.0.1;Port={_postgres.Porta};Username=postgres;Password={senha};Timeout=10";
            var limite = DateTime.UtcNow.AddSeconds(90);
            while (true)
            {
                try
                {
                    await using var c = new NpgsqlConnection(servidor + ";Database=postgres");
                    await c.OpenAsync();
                    return servidor;
                }
                catch when (DateTime.UtcNow < limite) { await Task.Delay(500); }
            }
        }

        private async Task<string> PrepararRedisAsync()
        {
            var externo = Environment.GetEnvironmentVariable("TRATOO_SEGURANCA_REDIS");
            if (!string.IsNullOrWhiteSpace(externo))
                return externo;

            const string senha = "teste_redis_seguranca";
            _redis = ContainerDocker.Iniciar("redis:7-alpine", 6379,
                comando: new[] { "redis-server", "--requirepass", senha, "--save", "", "--appendonly", "no" });
            var conexao = $"127.0.0.1:{_redis.Porta},password={senha},abortConnect=false,connectTimeout=2000,asyncTimeout=2000";
            var limite = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                try
                {
                    using var m = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(conexao.Replace("abortConnect=false", "abortConnect=true"));
                    await m.GetDatabase().PingAsync();
                    return conexao;
                }
                catch when (DateTime.UtcNow < limite) { await Task.Delay(300); }
            }
        }

        private static async Task<string> CriarBancoAsync(string servidor, string nome)
        {
            var b = new NpgsqlConnectionStringBuilder(servidor) { Database = "postgres" };
            await using (var c = new NpgsqlConnection(b.ConnectionString))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE {nome}", c);
                await cmd.ExecuteNonQueryAsync();
            }
            b.Database = nome;
            return b.ConnectionString;
        }

        private async Task<ProcessoApiReal> IniciarAsync(string nome, Dictionary<string, string?> ambiente, string env)
        {
            var p = await ProcessoApiReal.IniciarAsync(nome, ambiente, env);
            _processos.Add(p);
            return p;
        }

        public TratooContext Db() => SeedIntegracao.Contexto(Conexao);

        // ─────────────────────────────────────────────────────────────────────
        // Tokens forjados (para provar que a API os RECUSA)
        // ─────────────────────────────────────────────────────────────────────

        public static string ForjarToken(Usuario usuario, string? sid, string chave, DateTime expira,
            DateTime? naoAntes = null, IEnumerable<string>? papeisExtras = null, string? emissor = null)
        {
            var bytes = Encoding.UTF8.GetBytes(chave);
            var credenciais = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(bytes) { KeyId = Domain.Features.Auth.ChavesJwt.Kid(bytes) },
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

            var claims = new List<System.Security.Claims.Claim>
            {
                new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, usuario.Email),
                new(System.Security.Claims.ClaimTypes.Role, usuario.TipoUsuario.ToString()),
                new("perfilCompleto", "true"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };
            if (sid is not null) claims.Add(new(JwtRegisteredClaimNames.Sid, sid));
            foreach (var papel in papeisExtras ?? Array.Empty<string>())
                claims.Add(new(System.Security.Claims.ClaimTypes.Role, papel));

            var token = new JwtSecurityToken(emissor ?? Emissor, emissor ?? Emissor, claims,
                notBefore: naoAntes ?? expira.AddMinutes(-15), expires: expira, signingCredentials: credenciais);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>JWT com <c>alg: none</c> (sem assinatura) e as mesmas claims de um token real.</summary>
        public static string TokenSemAssinatura(string tokenReal)
        {
            var partes = tokenReal.Split('.');
            var cabecalho = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
            return $"{cabecalho}.{partes[1]}.";
        }

        private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public async Task DisposeAsync()
        {
            foreach (var p in _processos)
                p.Dispose();
            if (Externo is not null) await Externo.DisposeAsync();
            if (_postgres is not null) await _postgres.DisposeAsync();
            if (_redis is not null) await _redis.DisposeAsync();
        }
    }

    public sealed record Cenario(int ContratanteId, int PrestadorId, int ProjetoId, Guid PropostaId, Guid ContratoId)
    {
        internal static Cenario De(SeedIntegracao.Cenario c) => new(c.ContratanteId, c.PrestadorId, c.ProjetoId, c.PropostaId, c.ContratoId);
    }

    /// <summary>Massa de dados semeada direto no banco (senhas conhecidas, perfis completos).</summary>
    public sealed class Massa
    {
        public required Usuario ContratanteA { get; init; }
        public required Usuario PrestadorA { get; init; }
        public required Usuario ContratanteB { get; init; }
        public required Usuario PrestadorB { get; init; }
        public required Usuario ContratanteD { get; init; }
        public required Usuario PrestadorD { get; init; }
        public required Usuario Admin { get; init; }
        public required Usuario ContratanteVarredura { get; init; }
        public required Usuario PrestadorVarredura { get; init; }

        public required Cenario CenarioA { get; init; }
        public required Cenario CenarioB { get; init; }
        public required Cenario CenarioD { get; init; }
        public required Guid PagamentoA { get; init; }
        public required Guid PagamentoD { get; init; }
        public required Guid DisputaD { get; init; }
        public required int ProjetoRascunhoA { get; init; }
        public required int ProjetoPrivadoA { get; init; }

        public static async Task<Massa> CriarAsync(string conexao)
        {
            var a = await SeedIntegracao.CriarAsync(conexao);
            var b = await SeedIntegracao.CriarAsync(conexao);
            var d = await SeedIntegracao.CriarAsync(conexao);
            var v = await SeedIntegracao.CriarAsync(conexao);

            await using var db = SeedIntegracao.Contexto(conexao);
            var hash = BCrypt.Net.BCrypt.HashPassword(ApiRealFixture.Senha);
            await db.Usuarios.ExecuteUpdateAsync(s => s.SetProperty(u => u.SenhaHash, hash));

            var admin = new Contratante
            {
                Nome = "Admin Segurança",
                Email = $"admin.{Guid.NewGuid():N}"[..20] + "@teste.local",
                SenhaHash = hash,
                TipoUsuario = TipoUsuario.Contratante,
                Status = StatusUsuario.Active,
                IdentidadeVerificada = true,
                IsAdmin = true,
                TipoPessoa = TipoPessoa.PessoaFisica,
                Endereco = new Endereco { Cep = "01310-100", Logradouro = "Av. Paulista", Numero = "1", Bairro = "Bela Vista", Cidade = "São Paulo", Estado = "SP" }
            };
            admin.VerificarPerfilMinimo();
            db.Contratantes.Add(admin);

            var pagamentoA = NovoPagamento(a.ContratoId, StatusPagamento.Retido);
            var pagamentoD = NovoPagamento(d.ContratoId, StatusPagamento.EmDisputa);
            db.Pagamentos.AddRange(pagamentoA, pagamentoD);
            await db.SaveChangesAsync();

            var disputa = new DisputaPagamento
            {
                PagamentoId = pagamentoD.Id,
                AbertoPorId = d.ContratanteId,
                Motivo = "Entrega não corresponde ao escopo combinado (massa dos testes de segurança).",
                Status = StatusDisputa.Aberta
            };
            db.Add(disputa);

            var rascunho = NovoProjeto(a.ContratanteId, StatusProjeto.Rascunho, VisibilidadeProjeto.Publico, "RASCUNHO-SIGILOSO");
            var privado = NovoProjeto(a.ContratanteId, StatusProjeto.Aberto, VisibilidadeProjeto.Privado, "PRIVADO-SIGILOSO");
            db.Projetos.AddRange(rascunho, privado);
            await db.SaveChangesAsync();

            async Task<Usuario> U(int id) => await db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == id);

            return new Massa
            {
                ContratanteA = await U(a.ContratanteId), PrestadorA = await U(a.PrestadorId),
                ContratanteB = await U(b.ContratanteId), PrestadorB = await U(b.PrestadorId),
                ContratanteD = await U(d.ContratanteId), PrestadorD = await U(d.PrestadorId),
                ContratanteVarredura = await U(v.ContratanteId), PrestadorVarredura = await U(v.PrestadorId),
                Admin = await U(admin.Id),
                CenarioA = Cenario.De(a), CenarioB = Cenario.De(b), CenarioD = Cenario.De(d),
                PagamentoA = pagamentoA.Id, PagamentoD = pagamentoD.Id, DisputaD = disputa.Id,
                ProjetoRascunhoA = rascunho.Id, ProjetoPrivadoA = privado.Id
            };
        }

        private static Pagamento NovoPagamento(Guid contratoId, StatusPagamento status) => new()
        {
            ContratoServicoId = contratoId,
            ValorBruto = 1200m,
            Status = status,
            Metodo = MetodoPagamento.Pix,
            Gateway = "Asaas",
            GatewayPagamentoId = "pay_seg_" + Guid.NewGuid().ToString("N")[..8],
            AsaasClienteId = "cus_seg",
            StatusGateway = "RECEIVED",
            PagoEm = DateTime.UtcNow.AddDays(-2),
            LiberacaoAutomaticaEm = DateTime.UtcNow.AddDays(30)
        };

        private static Projeto NovoProjeto(int contratanteId, StatusProjeto status, VisibilidadeProjeto vis, string marcador) => new()
        {
            ContratanteId = contratanteId,
            Titulo = $"Projeto {marcador}",
            Descricao = $"Descrição confidencial {marcador} — não deve aparecer para terceiros.",
            Categoria = CategoriaProjet.Design,
            OrcamentoMin = 500m,
            OrcamentoMax = 900m,
            PrazoEntrega = DateTime.UtcNow.AddDays(20),
            Status = status,
            Visibilidade = vis,
            Publicado = status != StatusProjeto.Rascunho,
            PublicadoEm = status != StatusProjeto.Rascunho ? DateTime.UtcNow.AddDays(-1) : null
        };
    }

    /// <summary>Um processo <c>dotnet Tratoo.API.dll</c> em execução, escutando numa porta livre.</summary>
    public sealed class ProcessoApiReal : IDisposable
    {
        private readonly Process _processo;
        private readonly StringBuilder _saida = new();
        public string Nome { get; }
        public Uri BaseUrl { get; }
        public HttpClient Http { get; }

        private ProcessoApiReal(string nome, Process p, int porta)
        {
            Nome = nome;
            _processo = p;
            BaseUrl = new Uri($"http://127.0.0.1:{porta}");
            Http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
            {
                BaseAddress = BaseUrl,
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public string Saida { get { lock (_saida) return _saida.ToString(); } }

        public static async Task<ProcessoApiReal> IniciarAsync(string nome, IDictionary<string, string?> ambiente, string environmentName)
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "Tratoo.API.dll");
            Assert.True(File.Exists(dll), $"Tratoo.API.dll não encontrado em {AppContext.BaseDirectory}");

            // Diretório limpo (nenhum appsettings.json real é lido) com um wwwroot vazio.
            var cwd = Directory.CreateTempSubdirectory($"tratoo-seg-{nome}-").FullName;
            Directory.CreateDirectory(Path.Combine(cwd, "wwwroot"));

            var porta = ProcessoApi.PortaLivre();
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(dll);
            foreach (var k in psi.Environment.Keys.Where(k =>
                         k.Contains("__", StringComparison.Ordinal) || k.StartsWith("ASPNETCORE_", StringComparison.Ordinal)).ToList())
                psi.Environment.Remove(k);
            foreach (var (k, v) in ambiente) psi.Environment[k] = v;
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = environmentName;
            psi.Environment["PORT"] = porta.ToString();

            var p = Process.Start(psi)!;
            var api = new ProcessoApiReal(nome, p, porta);
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (api._saida) api._saida.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (api._saida) api._saida.AppendLine(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var limite = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < limite)
            {
                if (p.HasExited)
                    throw new InvalidOperationException($"API '{nome}' encerrou na subida (exit {p.ExitCode}):\n{api.Saida}");
                try
                {
                    using var r = await api.Http.GetAsync("/health/ready");
                    if (r.StatusCode == HttpStatusCode.OK) return api;
                }
                catch (HttpRequestException) { }
                await Task.Delay(300);
            }
            throw new TimeoutException($"API '{nome}' não ficou pronta em 120 s:\n{api.Saida}");
        }

        public void Dispose()
        {
            try { if (!_processo.HasExited) _processo.Kill(entireProcessTree: true); } catch { }
            _processo.Dispose();
            Http.Dispose();
        }
    }

    [CollectionDefinition(Nome)]
    public sealed class ColecaoApiReal : ICollectionFixture<ApiRealFixture>
    {
        public const string Nome = "API real (segurança)";
    }

    public sealed class FactApiRealAttribute : FactAttribute
    {
        public FactApiRealAttribute()
        {
            if (!ApiRealFixture.Disponivel && !DockerCli.Exigido)
                Skip = "Sem Docker nem TRATOO_SEGURANCA_PG/TRATOO_SEGURANCA_REDIS: testes de segurança contra a API real ignorados.";
        }
    }

    public sealed class TheoryApiRealAttribute : TheoryAttribute
    {
        public TheoryApiRealAttribute()
        {
            if (!ApiRealFixture.Disponivel && !DockerCli.Exigido)
                Skip = "Sem Docker nem TRATOO_SEGURANCA_PG/TRATOO_SEGURANCA_REDIS: testes de segurança contra a API real ignorados.";
        }
    }

    /// <summary>
    /// Reproduz um ACHADO ainda aberto da auditoria (Docs/security-audit/REPORT.md). O teste
    /// afirma o comportamento SEGURO, então falha enquanto a vulnerabilidade existir. Fica
    /// ignorado por padrão para não travar o CI; <c>TRATOO_REPRODUZIR_ACHADOS=1</c> o executa.
    /// A correção do achado remove este atributo (o teste passa a ser obrigatório).
    /// </summary>
    public sealed class FactAchadoAttribute : FactAttribute
    {
        public FactAchadoAttribute(string achado)
        {
            if (!ApiRealFixture.Disponivel && !DockerCli.Exigido)
                Skip = "Sem infraestrutura para a API real.";
            else if (Environment.GetEnvironmentVariable("TRATOO_REPRODUZIR_ACHADOS") != "1")
                Skip = $"Achado {achado} em aberto (Docs/security-audit/REPORT.md). Rode com TRATOO_REPRODUZIR_ACHADOS=1 para reproduzi-lo.";
        }
    }

    public sealed class TheoryAchadoAttribute : TheoryAttribute
    {
        public TheoryAchadoAttribute(string achado)
        {
            if (!ApiRealFixture.Disponivel && !DockerCli.Exigido)
                Skip = "Sem infraestrutura para a API real.";
            else if (Environment.GetEnvironmentVariable("TRATOO_REPRODUZIR_ACHADOS") != "1")
                Skip = $"Achado {achado} em aberto (Docs/security-audit/REPORT.md). Rode com TRATOO_REPRODUZIR_ACHADOS=1 para reproduzi-lo.";
        }
    }
}
