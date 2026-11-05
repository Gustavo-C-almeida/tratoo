using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tratoo.API.Requests;
using Tratoo.Domain.Data;
using Tratoo.Domain.Enums;
using Tratoo.Domain.Exceptions;
using Tratoo.Domain.Features.Auth;
using Tratoo.Domain.Features.Contratos;
using Tratoo.Domain.Features.IA;
using Tratoo.Domain.Features.Infrastructure;
using Tratoo.Domain.Features.Mensagens;
using Tratoo.Domain.Features.Pagamentos;
using Tratoo.Domain.Features.Perfis;
using Tratoo.Domain.Features.Projetos;
using Tratoo.Domain.Features.Propostas;
using Tratoo.Domain.Features.Storage;
using Tratoo.Domain.Models.Financeiro;
using Xunit;

namespace Tratoo.Tests.Integracao
{
    /// <summary>
    /// Regressão de <c>Cannot write DateTime with Kind=Unspecified to PostgreSQL type
    /// 'timestamp with time zone'</c>, contra Postgres REAL: é o driver que recusa o valor, então
    /// só um banco de verdade prova a correção. Cada teste entrega à camada de serviço a data
    /// como o front a envia.
    /// </summary>
    [Collection(ColecaoPostgres.Nome)]
    public class DatasPersistenciaTests
    {
        private static readonly DateTime DiaFuturo = DateTime.UtcNow.Date.AddDays(45);
        private static readonly string DiaFuturoTexto = DiaFuturo.ToString("yyyy-MM-dd");
        private static readonly DateTime DiaFuturoSemFuso = DateTime.SpecifyKind(DiaFuturo, DateTimeKind.Unspecified);

        private readonly PostgresFixture _pg;

        public DatasPersistenciaTests(PostgresFixture pg) => _pg = pg;

        private static ServiceProvider Servicos(string conexao)
        {
            var s = new ServiceCollection();
            s.AddLogging();
            s.AddDbContext<TratooContext>(o => o.UseNpgsql(conexao));
            s.AddDbContext<VectorContext>(o => o.UseNpgsql(conexao, n => n.UseVector()));

            s.AddScoped<IProjetoRepository, ProjetoRepository>();
            s.AddScoped<IContratanteRepository, ContratanteRepository>();
            s.AddScoped<IIdentidadeRepository, IdentidadeRepository>();
            s.AddScoped<IPrestadorRepository, PrestadorRepository>();
            s.AddScoped<IUsuarioRepository, UsuarioRepository>();
            s.AddScoped<IPropostaProjetoRepository, PropostaProjetoRepository>();
            s.AddScoped<IConviteProjetoRepository, ConviteProjetoRepository>();
            s.AddScoped<IPagamentoRepository, PagamentoRepository>();

            s.AddScoped<ProjetoService>();
            s.AddScoped<PropostaProjetoService>();
            s.AddScoped<ConviteProjetoService>();
            s.AddScoped<ExperienciaService>();
            s.AddScoped<CertificacaoService>();
            s.AddScoped<FinanceiroService>();
            s.AddScoped<AdminDisputaService>();
            s.AddScoped<ProjetoIndexadorService>();
            s.AddScoped<PrestadorIndexadorService>();
            s.AddScoped<TextoNormalizadorService>();

            s.AddSingleton(Falso<IEmbeddingService>.Criar());
            s.AddSingleton(Falso<IEmailService>.Criar());
            s.AddSingleton(Falso<IArquivoStorageService>.Criar());
            s.AddSingleton(Falso<IContratoServicoService>.Criar());
            s.AddSingleton(Falso<IPagamentoService>.Criar());

            return s.BuildServiceProvider();
        }

        // ── Projeto: o erro relatado ────────────────────────────────────────────

        [FactComDocker]
        public async Task CriarProjeto_ComDataDoInputDoFront_GravaODiaInformado()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            using var servicos = Servicos(conexao);

            // Mesmo corpo do POST /api/projects: o prazo chega como "yyyy-MM-dd".
            var request = JsonSerializer.Deserialize<CriarProjetoRequest>(
                $$"""
                {
                  "titulo": "Projeto de teste com prazo",
                  "descricao": "{{new string('x', 120)}}",
                  "categoria": "Design",
                  "orcamentoMin": 100,
                  "orcamentoMax": 200,
                  "prazoEntrega": "{{DiaFuturoTexto}}"
                }
                """,
                OpcoesDaApi)!;
            Assert.Equal(DateTimeKind.Unspecified, request.PrazoEntrega.Kind);

            using var escopo = servicos.CreateScope();
            var criado = await escopo.ServiceProvider.GetRequiredService<ProjetoService>().CriarAsync(new CriarProjetoDTO
            {
                ContratanteId = cenario.ContratanteId,
                Titulo = request.Titulo,
                Descricao = request.Descricao,
                Categoria = request.Categoria,
                OrcamentoMin = request.OrcamentoMin,
                OrcamentoMax = request.OrcamentoMax,
                PrazoEntrega = request.PrazoEntrega
            });

            await using var db = SeedIntegracao.Contexto(conexao);
            var gravado = await db.Projetos.AsNoTracking().SingleAsync(p => p.Id == criado.Id);
            Assert.Equal(DateTimeKind.Utc, gravado.PrazoEntrega.Kind);
            Assert.Equal(DiaFuturo, gravado.PrazoEntrega);
        }

        [FactComDocker]
        public async Task CriarProjeto_ComPrazoNoPassado_ContinuaRecusado()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            using var servicos = Servicos(conexao);

            using var escopo = servicos.CreateScope();
            var erro = await Assert.ThrowsAsync<NegocioException>(() =>
                escopo.ServiceProvider.GetRequiredService<ProjetoService>().CriarAsync(
                    NovoProjeto(cenario.ContratanteId, DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-2), DateTimeKind.Unspecified))));

            Assert.Contains("data futura", erro.Message);
        }

        [FactComDocker]
        public async Task BuscarProjetos_ComFiltroDePrazoSemFuso_NaoQuebra()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            using var servicos = Servicos(conexao);

            using var escopo = servicos.CreateScope();
            var resultado = await escopo.ServiceProvider.GetRequiredService<ProjetoService>()
                .BuscarAsync(new FiltrosProjetoDTO { PrazoAte = DiaFuturoSemFuso });

            Assert.NotNull(resultado);
        }

        [FactComDocker]
        public async Task BuscarProjetos_ComFiltroPrazoAte_TrazSoQuemEntregaAteODiaInclusive()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            var diaAnterior = await CriarProjetoAbertoAsync(conexao, cenario.ContratanteId, DiaFuturo.AddDays(-1));
            var noDia = await CriarProjetoAbertoAsync(conexao, cenario.ContratanteId, DiaFuturo);
            var noDiaComHorario = await CriarProjetoAbertoAsync(conexao, cenario.ContratanteId, DiaFuturo.AddHours(15));
            var diaSeguinte = await CriarProjetoAbertoAsync(conexao, cenario.ContratanteId, DiaFuturo.AddDays(1));
            using var servicos = Servicos(conexao);

            using var escopo = servicos.CreateScope();
            var resultado = await escopo.ServiceProvider.GetRequiredService<ProjetoService>()
                .BuscarAsync(new FiltrosProjetoDTO { PrazoAte = DiaFuturoSemFuso });

            var encontrados = resultado.Itens.Select(p => p.Id).ToList();
            Assert.Contains(diaAnterior, encontrados);
            Assert.Contains(noDia, encontrados);
            Assert.Contains(noDiaComHorario, encontrados);
            Assert.DoesNotContain(diaSeguinte, encontrados);
        }

        // ── Proposta e convite ──────────────────────────────────────────────────

        [FactComDocker]
        public async Task CriarRascunhoDeProposta_ComDatasSemFuso_RespeitaASemanticaDeCadaCampo()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            var projetoId = await CriarProjetoAbertoAsync(conexao, cenario.ContratanteId);
            using var servicos = Servicos(conexao);

            var validoAte = DateTime.SpecifyKind(DiaFuturo.AddHours(15), DateTimeKind.Unspecified);
            using var escopo = servicos.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<PropostaProjetoService>().CriarRascunhoAsync(new CriarRascunhoPropostaDTO
            {
                ProjetoId = projetoId,
                PrestadorId = cenario.PrestadorId,
                ValidoAte = validoAte,
                PrazoTotal = DiaFuturoSemFuso.AddHours(15),
                Objetivo = "Objetivo da proposta de teste com tamanho suficiente.",
                Escopo = "Escopo da proposta de teste, descrito com o tamanho mínimo exigido.",
                ValorTotal = 500m,
                RevisoesInclusas = 1
            });

            await using var db = SeedIntegracao.Contexto(conexao);
            var proposta = await db.PropostasProjeto.AsNoTracking().SingleAsync(p => p.ProjetoId == projetoId);
            var versao = await db.PropostaVersoes.AsNoTracking().SingleAsync(v => v.PropostaId == proposta.Id);

            Assert.Equal(DiaFuturo.AddHours(15), proposta.ValidoAte); // instante: mantém o horário
            Assert.Equal(DiaFuturo, versao.PrazoTotal);               // calendário: só o dia
        }

        [FactComDocker]
        public async Task EnviarConvite_ComPrazoDesejadoSemFuso_GravaODia()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            var projetoId = await CriarProjetoAbertoAsync(conexao, cenario.ContratanteId);
            using var servicos = Servicos(conexao);

            using var escopo = servicos.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<ConviteProjetoService>().EnviarAsync(new CriarConviteProjetoDTO
            {
                ProjetoId = projetoId,
                ContratanteId = cenario.ContratanteId,
                PrestadorId = cenario.PrestadorId,
                MensagemInicial = "Mensagem do convite de teste.",
                PrazoDesejado = DiaFuturoSemFuso
            });

            await using var db = SeedIntegracao.Contexto(conexao);
            var convite = await db.ConvitesProjeto.AsNoTracking().SingleAsync(c => c.ProjetoId == projetoId);
            Assert.Equal(DiaFuturo, convite.PrazoDesejado);
        }

        // ── Perfil do prestador ─────────────────────────────────────────────────

        [FactComDocker]
        public async Task Experiencia_ComDatasDoInputDoFront_GravaEEdita()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            using var servicos = Servicos(conexao);

            var inicio = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var fim = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Unspecified);

            using (var escopo = servicos.CreateScope())
            {
                await escopo.ServiceProvider.GetRequiredService<ExperienciaService>().AdicionarAsync(new ExperienciaDTO
                {
                    PrestadorId = cenario.PrestadorId, Empresa = "Empresa", Cargo = "Cargo",
                    DataInicio = inicio, DataFim = fim
                });
            }

            int id;
            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                var gravada = await db.Set<Tratoo.Domain.Models.Prestador.ExperienciaPrestador>().AsNoTracking()
                    .SingleAsync(e => e.PrestadorId == cenario.PrestadorId);
                Assert.Equal(DateTimeKind.Utc, gravada.DataInicio.Kind);
                Assert.Equal(new DateTime(2024, 3, 1), gravada.DataInicio);
                Assert.Equal(new DateTime(2025, 6, 30), gravada.DataFim);
                id = gravada.Id;
            }

            using (var escopo = servicos.CreateScope())
            {
                await escopo.ServiceProvider.GetRequiredService<ExperienciaService>().EditarAsync(new ExperienciaDTO
                {
                    Id = id, PrestadorId = cenario.PrestadorId, Empresa = "Empresa", Cargo = "Cargo",
                    DataInicio = inicio.AddMonths(1), DataFim = null, EmpregoAtual = true
                });
            }

            await using var dbFinal = SeedIntegracao.Contexto(conexao);
            var editada = await dbFinal.Set<Tratoo.Domain.Models.Prestador.ExperienciaPrestador>().AsNoTracking()
                .SingleAsync(e => e.Id == id);
            Assert.Equal(new DateTime(2024, 4, 1), editada.DataInicio);
            Assert.Null(editada.DataFim);
        }

        [FactComDocker]
        public async Task Experiencia_SemDataDeInicio_EhRecusada()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            using var servicos = Servicos(conexao);

            using var escopo = servicos.CreateScope();
            await Assert.ThrowsAsync<NegocioException>(() =>
                escopo.ServiceProvider.GetRequiredService<ExperienciaService>().AdicionarAsync(new ExperienciaDTO
                {
                    PrestadorId = cenario.PrestadorId, Empresa = "Empresa", Cargo = "Cargo"
                }));
        }

        [FactComDocker]
        public async Task Certificacao_ComDataLidaDoFormulario_Grava()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            using var servicos = Servicos(conexao);

            using var escopo = servicos.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<CertificacaoService>().AdicionarAsync(new CertificacaoDTO
            {
                PrestadorId = cenario.PrestadorId, Nome = "Certificado", Instituicao = "Instituição",
                DataEmissao = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Unspecified),
                DataValidade = new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Unspecified)
            });

            await using var db = SeedIntegracao.Contexto(conexao);
            var gravada = await db.Set<Tratoo.Domain.Models.Prestador.CertificacaoPrestador>().AsNoTracking()
                .SingleAsync(c => c.PrestadorId == cenario.PrestadorId);
            Assert.Equal(new DateTime(2025, 1, 15), gravada.DataEmissao);
            Assert.Equal(new DateTime(2027, 1, 15), gravada.DataValidade);
        }

        // ── Conta bancária: DateTime.Now gerava Kind=Local ──────────────────────

        [FactComDocker]
        public async Task ContaBancaria_CriarEAtualizar_NaoQuebra()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            var cenario = await SeedIntegracao.CriarAsync(conexao);
            using var servicos = Servicos(conexao);

            await using (var db = SeedIntegracao.Contexto(conexao))
            {
                await db.Set<ContaBancaria>().Where(c => c.PrestadorId == cenario.PrestadorId).ExecuteDeleteAsync();
            }

            var dados = new ContaBancariaDTO
            {
                Banco = "001", Agencia = "1234", Conta = "123456",
                Pix = "chave-de-teste", TipoPix = TipoPix.Aleatoria
            };

            using (var escopo = servicos.CreateScope())
                await escopo.ServiceProvider.GetRequiredService<FinanceiroService>().CadastrarConta(cenario.PrestadorId, dados);
            using (var escopo = servicos.CreateScope())
                await escopo.ServiceProvider.GetRequiredService<FinanceiroService>().CadastrarConta(cenario.PrestadorId, dados);

            await using var dbFinal = SeedIntegracao.Contexto(conexao);
            var conta = await dbFinal.Set<ContaBancaria>().AsNoTracking().SingleAsync(c => c.PrestadorId == cenario.PrestadorId);
            Assert.NotNull(conta.AtualizadoEm);
            Assert.Equal(DateTimeKind.Utc, conta.CriadoEm.Kind);
        }

        // ── Filtro do painel administrativo ─────────────────────────────────────

        [FactComDocker]
        public async Task ListarDisputas_ComFiltroSemFuso_NaoQuebra()
        {
            var conexao = await _pg.CriarBancoMigradoAsync();
            using var servicos = Servicos(conexao);

            using var escopo = servicos.CreateScope();
            var disputas = await escopo.ServiceProvider.GetRequiredService<AdminDisputaService>().ListarAsync(
                null, DiaFuturoSemFuso, DiaFuturoSemFuso.AddDays(1), null, null, null);

            Assert.Empty(disputas);
        }

        // ── Apoio ───────────────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions OpcoesDaApi = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private static CriarProjetoDTO NovoProjeto(int contratanteId, DateTime prazo) => new()
        {
            ContratanteId = contratanteId,
            Titulo = "Projeto de teste com prazo",
            Descricao = new string('x', 120),
            Categoria = CategoriaProjet.Design,
            OrcamentoMin = 100m,
            OrcamentoMax = 200m,
            PrazoEntrega = prazo
        };

        private static async Task<int> CriarProjetoAbertoAsync(string conexao, int contratanteId, DateTime? prazo = null)
        {
            await using var db = SeedIntegracao.Contexto(conexao);
            var projeto = new Tratoo.Domain.Models.Projeto
            {
                ContratanteId = contratanteId,
                Titulo = "Projeto aberto de teste",
                Descricao = new string('x', 120),
                Categoria = CategoriaProjet.Design,
                OrcamentoMin = 100m,
                OrcamentoMax = 200m,
                PrazoEntrega = prazo ?? DiaFuturo,
                Status = StatusProjeto.Aberto,
                Visibilidade = VisibilidadeProjeto.Publico,
                Publicado = true,
                PublicadoEm = DateTime.UtcNow
            };
            db.Projetos.Add(projeto);
            await db.SaveChangesAsync();
            return projeto.Id;
        }
    }
}
