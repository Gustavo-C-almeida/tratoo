using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class InitPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Acao = table.Column<string>(type: "text", nullable: false),
                    Ip = table.Column<string>(type: "text", nullable: false),
                    DataHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsentLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Versao = table.Column<string>(type: "text", nullable: false),
                    Ip = table.Column<string>(type: "text", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistoricosContrato",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoServicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Acao = table.Column<string>(type: "text", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    DataEvento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricosContrato", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: false),
                    TipoUsuario = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExcluidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MFA = table.Column<bool>(type: "boolean", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    PerfilMinimoCompleto = table.Column<bool>(type: "boolean", nullable: false),
                    IdentidadeVerificada = table.Column<bool>(type: "boolean", nullable: false),
                    TipoPessoa = table.Column<int>(type: "integer", nullable: true),
                    Endereco_Cep = table.Column<string>(type: "text", nullable: true),
                    Endereco_Logradouro = table.Column<string>(type: "text", nullable: true),
                    Endereco_Numero = table.Column<string>(type: "text", nullable: true),
                    Endereco_Complemento = table.Column<string>(type: "text", nullable: true),
                    Endereco_Bairro = table.Column<string>(type: "text", nullable: true),
                    Endereco_Cidade = table.Column<string>(type: "text", nullable: true),
                    Endereco_Estado = table.Column<string>(type: "text", nullable: true),
                    Telefone = table.Column<string>(type: "text", nullable: true),
                    AvaliacoesPrivado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "text", nullable: false),
                    TipoEvento = table.Column<string>(type: "text", nullable: false),
                    AsaasCobrancaId = table.Column<string>(type: "text", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    RecebidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    ProcessadoComSucesso = table.Column<bool>(type: "boolean", nullable: false),
                    ErroMensagem = table.Column<string>(type: "text", nullable: true),
                    ProcessadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contratantes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Segmento = table.Column<string>(type: "text", nullable: true),
                    NomeEmpresa = table.Column<string>(type: "text", nullable: true),
                    InscricaoEstadual = table.Column<string>(type: "text", nullable: true),
                    InscricaoMunicipal = table.Column<string>(type: "text", nullable: true),
                    DataAbertura = table.Column<DateOnly>(type: "date", nullable: true),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    SiteUrl = table.Column<string>(type: "text", nullable: true),
                    LinkedinUrl = table.Column<string>(type: "text", nullable: true),
                    EmailContato = table.Column<string>(type: "text", nullable: true),
                    PorQueTrabalharComigo = table.Column<string>(type: "text", nullable: true),
                    Disponibilidade = table.Column<int>(type: "integer", nullable: true),
                    IdiomasAceitosJson = table.Column<string>(type: "text", nullable: true),
                    TamanhoEquipe = table.Column<int>(type: "integer", nullable: true),
                    ExibirIdade = table.Column<bool>(type: "boolean", nullable: false),
                    PagadorVerificado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contratantes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contratantes_Usuarios_Id",
                        column: x => x.Id,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prestadores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    NomeFantasia = table.Column<string>(type: "text", nullable: true),
                    AreaEspecializacao = table.Column<string>(type: "text", nullable: true),
                    FuncaoExecutada = table.Column<string>(type: "text", nullable: true),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    LinkedinUrl = table.Column<string>(type: "text", nullable: true),
                    PortfolioUrl = table.Column<string>(type: "text", nullable: true),
                    TituloProfissional = table.Column<string>(type: "text", nullable: true),
                    FotoUrl = table.Column<string>(type: "text", nullable: true),
                    EmailContato = table.Column<string>(type: "text", nullable: true),
                    OutrosLinks = table.Column<string>(type: "text", nullable: true),
                    PorcentagemCompleto = table.Column<int>(type: "integer", nullable: false),
                    Disponivel = table.Column<bool>(type: "boolean", nullable: false),
                    DisponivelAPartirDe = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValorMinimoProjeto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AceitaParcelamento = table.Column<bool>(type: "boolean", nullable: true),
                    DisponibilidadesPrivado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prestadores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prestadores_Usuarios_Id",
                        column: x => x.Id,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReputacaoResumos",
                columns: table => new
                {
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    MediaGeral = table.Column<double>(type: "double precision", precision: 4, scale: 2, nullable: false),
                    TotalAvaliacoes = table.Column<int>(type: "integer", nullable: false),
                    Distribuicao1 = table.Column<int>(type: "integer", nullable: false),
                    Distribuicao2 = table.Column<int>(type: "integer", nullable: false),
                    Distribuicao3 = table.Column<int>(type: "integer", nullable: false),
                    Distribuicao4 = table.Column<int>(type: "integer", nullable: false),
                    Distribuicao5 = table.Column<int>(type: "integer", nullable: false),
                    UltimaAtualizacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReputacaoResumos", x => x.UsuarioId);
                    table.ForeignKey(
                        name: "FK_ReputacaoResumos_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserIdentities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CpfCnpjCriptografado = table.Column<string>(type: "text", nullable: false),
                    NomeLegal = table.Column<string>(type: "text", nullable: false),
                    NivelVerificacao = table.Column<int>(type: "integer", nullable: false),
                    ChavePixCriptografada = table.Column<string>(type: "text", nullable: true),
                    CpfRepresentanteLegalCriptografado = table.Column<string>(type: "text", nullable: true),
                    NomeRepresentanteLegal = table.Column<string>(type: "text", nullable: true),
                    CargoRepresentanteLegal = table.Column<string>(type: "text", nullable: true),
                    EmailRepresentanteLegal = table.Column<string>(type: "text", nullable: true),
                    TelefoneRepresentanteLegal = table.Column<string>(type: "text", nullable: true),
                    DataNascimento = table.Column<DateOnly>(type: "date", nullable: true),
                    ExibirIdade = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserIdentities_Usuarios_UserId",
                        column: x => x.UserId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CertificacoesPrestador",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    InstituicaoEmissora = table.Column<string>(type: "text", nullable: false),
                    DataEmissao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataValidade = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LinkVerificacao = table.Column<string>(type: "text", nullable: true),
                    ArquivoUrl = table.Column<string>(type: "text", nullable: true),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificacoesPrestador", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificacoesPrestador_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Competencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Nivel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Competencias_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContasBancarias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false),
                    Banco = table.Column<string>(type: "text", nullable: false),
                    Agencia = table.Column<string>(type: "text", nullable: false),
                    ContaCriptografada = table.Column<string>(type: "text", nullable: false),
                    PixChave = table.Column<string>(type: "text", nullable: false),
                    TipoPix = table.Column<int>(type: "integer", nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContasBancarias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContasBancarias_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DisponibilidadesHorario",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false),
                    DiaSemana = table.Column<int>(type: "integer", nullable: false),
                    HoraInicio = table.Column<TimeSpan>(type: "interval", nullable: false),
                    HoraFim = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisponibilidadesHorario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisponibilidadesHorario_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExperienciasPrestador",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false),
                    Empresa = table.Column<string>(type: "text", nullable: false),
                    Cargo = table.Column<string>(type: "text", nullable: false),
                    Atividades = table.Column<string>(type: "text", nullable: true),
                    DataInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataFim = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmpregoAtual = table.Column<bool>(type: "boolean", nullable: false),
                    Local = table.Column<string>(type: "text", nullable: true),
                    TipoContrato = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienciasPrestador", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperienciasPrestador_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfoliosPrestador",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LinkExterno = table.Column<string>(type: "text", nullable: true),
                    ArquivoUrl = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfoliosPrestador", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfoliosPrestador_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Projetos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContratanteId = table.Column<int>(type: "integer", nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    Categoria = table.Column<string>(type: "text", nullable: false),
                    OrcamentoMin = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrcamentoMax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PrazoEntrega = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Habilidades = table.Column<string>(type: "text", nullable: true),
                    NivelFreelancer = table.Column<string>(type: "text", nullable: true),
                    Visibilidade = table.Column<string>(type: "text", nullable: false),
                    Idioma = table.Column<string>(type: "text", nullable: false),
                    FreelancerSelecionadoId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Publicado = table.Column<bool>(type: "boolean", nullable: false),
                    PublicadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CanceladoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanceladoPorId = table.Column<int>(type: "integer", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "text", nullable: true),
                    TotalPropostas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projetos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projetos_Contratantes_ContratanteId",
                        column: x => x.ContratanteId,
                        principalTable: "Contratantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projetos_Prestadores_FreelancerSelecionadoId",
                        column: x => x.FreelancerSelecionadoId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetenciaCertificacao",
                columns: table => new
                {
                    CompetenciaId = table.Column<int>(type: "integer", nullable: false),
                    CertificacaoPrestadorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetenciaCertificacao", x => new { x.CompetenciaId, x.CertificacaoPrestadorId });
                    table.ForeignKey(
                        name: "FK_CompetenciaCertificacao_CertificacoesPrestador_Certificacao~",
                        column: x => x.CertificacaoPrestadorId,
                        principalTable: "CertificacoesPrestador",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetenciaCertificacao_Competencias_CompetenciaId",
                        column: x => x.CompetenciaId,
                        principalTable: "Competencias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetenciaExperiencia",
                columns: table => new
                {
                    CompetenciaId = table.Column<int>(type: "integer", nullable: false),
                    ExperienciaPrestadorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetenciaExperiencia", x => new { x.CompetenciaId, x.ExperienciaPrestadorId });
                    table.ForeignKey(
                        name: "FK_CompetenciaExperiencia_Competencias_CompetenciaId",
                        column: x => x.CompetenciaId,
                        principalTable: "Competencias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetenciaExperiencia_ExperienciasPrestador_ExperienciaPre~",
                        column: x => x.ExperienciaPrestadorId,
                        principalTable: "ExperienciasPrestador",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetenciaPortfolios",
                columns: table => new
                {
                    CompetenciaId = table.Column<int>(type: "integer", nullable: false),
                    PortfolioPrestadorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetenciaPortfolios", x => new { x.CompetenciaId, x.PortfolioPrestadorId });
                    table.ForeignKey(
                        name: "FK_CompetenciaPortfolios_Competencias_CompetenciaId",
                        column: x => x.CompetenciaId,
                        principalTable: "Competencias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetenciaPortfolios_PortfoliosPrestador_PortfolioPrestado~",
                        column: x => x.PortfolioPrestadorId,
                        principalTable: "PortfoliosPrestador",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContratosServico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjetoId = table.Column<int>(type: "integer", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratanteId = table.Column<int>(type: "integer", nullable: false),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false),
                    ConteudoJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AssinadoContratanteEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IpContratante = table.Column<string>(type: "text", nullable: true),
                    UserAgentContratante = table.Column<string>(type: "text", nullable: true),
                    AssinadoPrestadorEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IpPrestador = table.Column<string>(type: "text", nullable: true),
                    UserAgentPrestador = table.Column<string>(type: "text", nullable: true),
                    ConteudoHash = table.Column<string>(type: "text", nullable: true),
                    TemplateVersao = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PdfKey = table.Column<string>(type: "text", nullable: true),
                    EntregaRegistradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CanceladoPorId = table.Column<int>(type: "integer", nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContratosServico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContratosServico_Contratantes_ContratanteId",
                        column: x => x.ContratanteId,
                        principalTable: "Contratantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContratosServico_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContratosServico_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContratosServico_Usuarios_CanceladoPorId",
                        column: x => x.CanceladoPorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConvitesProjeto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjetoId = table.Column<int>(type: "integer", nullable: false),
                    ContratanteId = table.Column<int>(type: "integer", nullable: false),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false),
                    MensagemInicial = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OrcamentoSugerido = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PrazoDesejado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    RespondidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoRecusa = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConvitesProjeto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConvitesProjeto_Contratantes_ContratanteId",
                        column: x => x.ContratanteId,
                        principalTable: "Contratantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConvitesProjeto_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConvitesProjeto_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Avaliacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoServicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvaliadorId = table.Column<int>(type: "integer", nullable: false),
                    AvaliadoId = table.Column<int>(type: "integer", nullable: false),
                    Nota = table.Column<int>(type: "integer", nullable: true),
                    Comentario = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Publica = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublicadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    NotaPrazo = table.Column<byte>(type: "smallint", nullable: true),
                    NotaComunicacao = table.Column<byte>(type: "smallint", nullable: true),
                    NotaQualidade = table.Column<byte>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Avaliacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Avaliacoes_ContratosServico_ContratoServicoId",
                        column: x => x.ContratoServicoId,
                        principalTable: "ContratosServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Avaliacoes_Usuarios_AvaliadoId",
                        column: x => x.AvaliadoId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Avaliacoes_Usuarios_AvaliadorId",
                        column: x => x.AvaliadorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContratoSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DadosContratante = table.Column<string>(type: "text", nullable: false),
                    DadosPrestador = table.Column<string>(type: "text", nullable: false),
                    ConteudoFinal = table.Column<string>(type: "text", nullable: false),
                    CongeladoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContratoSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContratoSnapshots_ContratosServico_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "ContratosServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Entregas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoServicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DescricaoEntrega = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DataEntrega = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AprovadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AprovadorId = table.Column<int>(type: "integer", nullable: true),
                    MotivoRejeicao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RejeitadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entregas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entregas_ContratosServico_ContratoServicoId",
                        column: x => x.ContratoServicoId,
                        principalTable: "ContratosServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoricosAssinatura",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    Acao = table.Column<string>(type: "text", nullable: false),
                    Ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DataEvento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricosAssinatura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricosAssinatura_ContratosServico_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "ContratosServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pagamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoServicoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValorBruto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxaGateway = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PagoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LiberadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstornadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LiberacaoAutomaticaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Metodo = table.Column<string>(type: "text", nullable: false),
                    Gateway = table.Column<string>(type: "text", nullable: false),
                    GatewayPagamentoId = table.Column<string>(type: "text", nullable: true),
                    AsaasClienteId = table.Column<string>(type: "text", nullable: true),
                    AsaasTransferenciaId = table.Column<string>(type: "text", nullable: true),
                    StatusGateway = table.Column<string>(type: "text", nullable: true),
                    PixQrCodePayload = table.Column<string>(type: "text", nullable: true),
                    PixQrCodeImagem = table.Column<string>(type: "text", nullable: true),
                    PixQrCodeExpiracao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PayloadGateway = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pagamentos_ContratosServico_ContratoServicoId",
                        column: x => x.ContratoServicoId,
                        principalTable: "ContratosServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PropostasProjeto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjetoId = table.Column<int>(type: "integer", nullable: false),
                    PrestadorId = table.Column<int>(type: "integer", nullable: false),
                    SenderType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConviteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    VersaoAtual = table.Column<int>(type: "integer", nullable: false),
                    ValidoAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MotivoCancelamento = table.Column<string>(type: "text", nullable: true),
                    CanceladoPorId = table.Column<int>(type: "integer", nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropostasProjeto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropostasProjeto_ConvitesProjeto_ConviteId",
                        column: x => x.ConviteId,
                        principalTable: "ConvitesProjeto",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropostasProjeto_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropostasProjeto_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EntregaAnexos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntregaId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeArquivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ChaveR2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TipoArquivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TamanhoArquivo = table.Column<long>(type: "bigint", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    ExcluidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntregaAnexos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntregaAnexos_Entregas_EntregaId",
                        column: x => x.EntregaId,
                        principalTable: "Entregas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EntregaLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntregaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntregaLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntregaLinks_Entregas_EntregaId",
                        column: x => x.EntregaId,
                        principalTable: "Entregas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DisputasPagamento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PagamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AbertoPorId = table.Column<int>(type: "integer", nullable: false),
                    AbertoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: false),
                    EvidenciasJson = table.Column<string>(type: "text", nullable: true),
                    ResolvidoPorId = table.Column<int>(type: "integer", nullable: true),
                    ResolvidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotaResolucao = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputasPagamento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisputasPagamento_Pagamentos_PagamentoId",
                        column: x => x.PagamentoId,
                        principalTable: "Pagamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LedgerFinanceiro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PagamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    ReferenciaExterna = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    CriadoPorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerFinanceiro", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerFinanceiro_Pagamentos_PagamentoId",
                        column: x => x.PagamentoId,
                        principalTable: "Pagamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropostaVersoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Versao = table.Column<int>(type: "integer", nullable: false),
                    Objetivo = table.Column<string>(type: "text", nullable: false),
                    Escopo = table.Column<string>(type: "text", nullable: false),
                    Exclusoes = table.Column<string>(type: "text", nullable: true),
                    RevisoesInclusas = table.Column<int>(type: "integer", nullable: false),
                    PrazoTotal = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValorTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Entrada = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    FormaPagamento = table.Column<string>(type: "text", nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    MarcosJson = table.Column<string>(type: "text", nullable: true),
                    CriadoPor = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropostaVersoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropostaVersoes_PropostasProjeto_PropostaId",
                        column: x => x.PropostaId,
                        principalTable: "PropostasProjeto",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MensagensProjeto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjetoId = table.Column<int>(type: "integer", nullable: false),
                    RemetenteId = table.Column<int>(type: "integer", nullable: false),
                    PrestadorId = table.Column<int>(type: "integer", nullable: true),
                    Texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    PropostaVersaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnviadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensagensProjeto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MensagensProjeto_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MensagensProjeto_PropostaVersoes_PropostaVersaoId",
                        column: x => x.PropostaVersaoId,
                        principalTable: "PropostaVersoes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MensagensProjeto_Usuarios_RemetenteId",
                        column: x => x.RemetenteId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_DataHora",
                table: "AuditLogs",
                columns: new[] { "UserId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Avaliacoes_AvaliadoId",
                table: "Avaliacoes",
                column: "AvaliadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Avaliacoes_AvaliadorId",
                table: "Avaliacoes",
                column: "AvaliadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Avaliacoes_ContratoServicoId_AvaliadorId",
                table: "Avaliacoes",
                columns: new[] { "ContratoServicoId", "AvaliadorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Avaliacoes_Status",
                table: "Avaliacoes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CertificacoesPrestador_PrestadorId",
                table: "CertificacoesPrestador",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetenciaCertificacao_CertificacaoPrestadorId",
                table: "CompetenciaCertificacao",
                column: "CertificacaoPrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetenciaExperiencia_ExperienciaPrestadorId",
                table: "CompetenciaExperiencia",
                column: "ExperienciaPrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetenciaPortfolios_PortfolioPrestadorId",
                table: "CompetenciaPortfolios",
                column: "PortfolioPrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Competencias_PrestadorId",
                table: "Competencias",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentLogs_UserId",
                table: "ConsentLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasBancarias_PrestadorId",
                table: "ContasBancarias",
                column: "PrestadorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContratoSnapshots_ContratoId",
                table: "ContratoSnapshots",
                column: "ContratoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContratosServico_CanceladoPorId",
                table: "ContratosServico",
                column: "CanceladoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosServico_ContratanteId",
                table: "ContratosServico",
                column: "ContratanteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosServico_PrestadorId",
                table: "ContratosServico",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosServico_ProjetoId",
                table: "ContratosServico",
                column: "ProjetoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosServico_PropostaId",
                table: "ContratosServico",
                column: "PropostaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContratosServico_Status",
                table: "ContratosServico",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ConvitesProjeto_ContratanteId",
                table: "ConvitesProjeto",
                column: "ContratanteId");

            migrationBuilder.CreateIndex(
                name: "IX_ConvitesProjeto_PrestadorId_ProjetoId",
                table: "ConvitesProjeto",
                columns: new[] { "PrestadorId", "ProjetoId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConvitesProjeto_ProjetoId",
                table: "ConvitesProjeto",
                column: "ProjetoId");

            migrationBuilder.CreateIndex(
                name: "IX_ConvitesProjeto_Status",
                table: "ConvitesProjeto",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DisponibilidadesHorario_PrestadorId",
                table: "DisponibilidadesHorario",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputasPagamento_PagamentoId",
                table: "DisputasPagamento",
                column: "PagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_EntregaAnexos_EntregaId",
                table: "EntregaAnexos",
                column: "EntregaId");

            migrationBuilder.CreateIndex(
                name: "IX_EntregaLinks_EntregaId",
                table: "EntregaLinks",
                column: "EntregaId");

            migrationBuilder.CreateIndex(
                name: "IX_Entregas_ContratoServicoId",
                table: "Entregas",
                column: "ContratoServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienciasPrestador_PrestadorId",
                table: "ExperienciasPrestador",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricosAssinatura_ContratoId",
                table: "HistoricosAssinatura",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricosAssinatura_ContratoId_UsuarioId",
                table: "HistoricosAssinatura",
                columns: new[] { "ContratoId", "UsuarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricosContrato_ContratoServicoId",
                table: "HistoricosContrato",
                column: "ContratoServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerFinanceiro_PagamentoId",
                table: "LedgerFinanceiro",
                column: "PagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_PrestadorId_EnviadoEm",
                table: "MensagensProjeto",
                columns: new[] { "PrestadorId", "EnviadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_ProjetoId_PrestadorId_EnviadoEm",
                table: "MensagensProjeto",
                columns: new[] { "ProjetoId", "PrestadorId", "EnviadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_PropostaVersaoId",
                table: "MensagensProjeto",
                column: "PropostaVersaoId");

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_RemetenteId",
                table: "MensagensProjeto",
                column: "RemetenteId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_ContratoServicoId",
                table: "Pagamentos",
                column: "ContratoServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_GatewayPagamentoId",
                table: "Pagamentos",
                column: "GatewayPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_IdempotencyKey",
                table: "Pagamentos",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfoliosPrestador_PrestadorId",
                table: "PortfoliosPrestador",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Projetos_Categoria",
                table: "Projetos",
                column: "Categoria");

            migrationBuilder.CreateIndex(
                name: "IX_Projetos_ContratanteId",
                table: "Projetos",
                column: "ContratanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Projetos_FreelancerSelecionadoId",
                table: "Projetos",
                column: "FreelancerSelecionadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Projetos_Status",
                table: "Projetos",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PropostasProjeto_ConviteId",
                table: "PropostasProjeto",
                column: "ConviteId");

            migrationBuilder.CreateIndex(
                name: "IX_PropostasProjeto_PrestadorId_ProjetoId",
                table: "PropostasProjeto",
                columns: new[] { "PrestadorId", "ProjetoId" });

            migrationBuilder.CreateIndex(
                name: "IX_PropostasProjeto_ProjetoId",
                table: "PropostasProjeto",
                column: "ProjetoId");

            migrationBuilder.CreateIndex(
                name: "IX_PropostasProjeto_Status",
                table: "PropostasProjeto",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PropostaVersoes_PropostaId_Versao",
                table: "PropostaVersoes",
                columns: new[] { "PropostaId", "Versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_UserId",
                table: "UserIdentities",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookLogs_AsaasCobrancaId",
                table: "WebhookLogs",
                column: "AsaasCobrancaId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookLogs_ChaveIdempotencia",
                table: "WebhookLogs",
                column: "ChaveIdempotencia",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Avaliacoes");

            migrationBuilder.DropTable(
                name: "CompetenciaCertificacao");

            migrationBuilder.DropTable(
                name: "CompetenciaExperiencia");

            migrationBuilder.DropTable(
                name: "CompetenciaPortfolios");

            migrationBuilder.DropTable(
                name: "ConsentLogs");

            migrationBuilder.DropTable(
                name: "ContasBancarias");

            migrationBuilder.DropTable(
                name: "ContratoSnapshots");

            migrationBuilder.DropTable(
                name: "DisponibilidadesHorario");

            migrationBuilder.DropTable(
                name: "DisputasPagamento");

            migrationBuilder.DropTable(
                name: "EntregaAnexos");

            migrationBuilder.DropTable(
                name: "EntregaLinks");

            migrationBuilder.DropTable(
                name: "HistoricosAssinatura");

            migrationBuilder.DropTable(
                name: "HistoricosContrato");

            migrationBuilder.DropTable(
                name: "LedgerFinanceiro");

            migrationBuilder.DropTable(
                name: "MensagensProjeto");

            migrationBuilder.DropTable(
                name: "ReputacaoResumos");

            migrationBuilder.DropTable(
                name: "UserIdentities");

            migrationBuilder.DropTable(
                name: "WebhookLogs");

            migrationBuilder.DropTable(
                name: "CertificacoesPrestador");

            migrationBuilder.DropTable(
                name: "ExperienciasPrestador");

            migrationBuilder.DropTable(
                name: "Competencias");

            migrationBuilder.DropTable(
                name: "PortfoliosPrestador");

            migrationBuilder.DropTable(
                name: "Entregas");

            migrationBuilder.DropTable(
                name: "Pagamentos");

            migrationBuilder.DropTable(
                name: "PropostaVersoes");

            migrationBuilder.DropTable(
                name: "ContratosServico");

            migrationBuilder.DropTable(
                name: "PropostasProjeto");

            migrationBuilder.DropTable(
                name: "ConvitesProjeto");

            migrationBuilder.DropTable(
                name: "Projetos");

            migrationBuilder.DropTable(
                name: "Contratantes");

            migrationBuilder.DropTable(
                name: "Prestadores");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}
