using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class Refatoracao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Acao = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataHora = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsentLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Versao = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SenhaHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TipoUsuario = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MFA = table.Column<bool>(type: "bit", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    PerfilMinimoCompleto = table.Column<bool>(type: "bit", nullable: false),
                    IdentidadeVerificada = table.Column<bool>(type: "bit", nullable: false),
                    TipoPessoa = table.Column<int>(type: "int", nullable: true),
                    Endereco_Cep = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Endereco_Logradouro = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Endereco_Numero = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Endereco_Complemento = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Endereco_Bairro = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Endereco_Cidade = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Endereco_Estado = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Telefone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AvaliacoesPrivado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TipoEvento = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AsaasCobrancaId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecebidoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ProcessadoComSucesso = table.Column<bool>(type: "bit", nullable: false),
                    ErroMensagem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessadoEm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contratantes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Segmento = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NomeEmpresa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InscricaoEstadual = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InscricaoMunicipal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataAbertura = table.Column<DateOnly>(type: "date", nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SiteUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkedinUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExibirIdade = table.Column<bool>(type: "bit", nullable: false),
                    PagadorVerificado = table.Column<bool>(type: "bit", nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false),
                    NomeFantasia = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AreaEspecializacao = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FuncaoExecutada = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkedinUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PortfolioUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TituloProfissional = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FotoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GitHubUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailContato = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutrosLinks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PorcentagemCompleto = table.Column<int>(type: "int", nullable: false),
                    Disponivel = table.Column<bool>(type: "bit", nullable: false),
                    DisponivelAPartirDe = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValorHora = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ValorMinimoProjeto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AceitaParcelamento = table.Column<bool>(type: "bit", nullable: true),
                    DisponibilidadesPrivado = table.Column<bool>(type: "bit", nullable: false)
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
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    MediaGeral = table.Column<double>(type: "float(4)", precision: 4, scale: 2, nullable: false),
                    TotalAvaliacoes = table.Column<int>(type: "int", nullable: false),
                    Distribuicao1 = table.Column<int>(type: "int", nullable: false),
                    Distribuicao2 = table.Column<int>(type: "int", nullable: false),
                    Distribuicao3 = table.Column<int>(type: "int", nullable: false),
                    Distribuicao4 = table.Column<int>(type: "int", nullable: false),
                    Distribuicao5 = table.Column<int>(type: "int", nullable: false),
                    UltimaAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CpfCnpjCriptografado = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NomeLegal = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NivelVerificacao = table.Column<int>(type: "int", nullable: false),
                    ChavePixCriptografada = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CpfRepresentanteLegalCriptografado = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NomeRepresentanteLegal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CargoRepresentanteLegal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailRepresentanteLegal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TelefoneRepresentanteLegal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataNascimento = table.Column<DateOnly>(type: "date", nullable: true),
                    ExibirIdade = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InstituicaoEmissora = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataEmissao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataValidade = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LinkVerificacao = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PrestadorId = table.Column<int>(type: "int", nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Nivel = table.Column<int>(type: "int", nullable: false)
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
                name: "ContaBancaria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    Banco = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Agencia = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContaCriptografada = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PixChave = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TipoPix = table.Column<int>(type: "int", nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContaBancaria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContaBancaria_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DisponibilidadesHorario",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    DiaSemana = table.Column<int>(type: "int", nullable: false),
                    HoraInicio = table.Column<TimeSpan>(type: "time", nullable: false),
                    HoraFim = table.Column<TimeSpan>(type: "time", nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    Empresa = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Cargo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Atividades = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataFim = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmpregoAtual = table.Column<bool>(type: "bit", nullable: false),
                    Local = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TipoContrato = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LinkExterno = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ArquivoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
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
                name: "PrestadorEmbeddings",
                columns: table => new
                {
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextoNormalizado = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModeloVersao = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "all-MiniLM-L6-v2"),
                    IndexadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrestadorEmbeddings", x => x.PrestadorId);
                    table.ForeignKey(
                        name: "FK_PrestadorEmbeddings_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Projetos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContratanteId = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Categoria = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TipoOrcamento = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrcamentoMin = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OrcamentoMax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PrazoEntrega = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Habilidades = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NivelFreelancer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Visibilidade = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Idioma = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NumFreelancersDesejados = table.Column<int>(type: "int", nullable: false),
                    FreelancerSelecionadoId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Publicado = table.Column<bool>(type: "bit", nullable: false),
                    PublicadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CanceladoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalPropostas = table.Column<int>(type: "int", nullable: false)
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
                name: "Propostas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    ContratanteId = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Entregaveis = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CondicoesGerais = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DireitosUsoImagem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DireitosUsoEntregavel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrazoEntrega = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrazoResposta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondidaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoRecusa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContratoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataCancelamento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TipoCobranca = table.Column<int>(type: "int", nullable: false),
                    ValorTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ValorHora = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    HorasEstimadas = table.Column<int>(type: "int", nullable: true),
                    ValorMensal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DuracaoMeses = table.Column<int>(type: "int", nullable: true),
                    AtualizadaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Propostas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Propostas_Contratantes_ContratanteId",
                        column: x => x.ContratanteId,
                        principalTable: "Contratantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Propostas_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetenciaCertificacao",
                columns: table => new
                {
                    CompetenciaId = table.Column<int>(type: "int", nullable: false),
                    CertificacaoPrestadorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetenciaCertificacao", x => new { x.CompetenciaId, x.CertificacaoPrestadorId });
                    table.ForeignKey(
                        name: "FK_CompetenciaCertificacao_CertificacoesPrestador_CertificacaoPrestadorId",
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
                    CompetenciaId = table.Column<int>(type: "int", nullable: false),
                    ExperienciaPrestadorId = table.Column<int>(type: "int", nullable: false)
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
                        name: "FK_CompetenciaExperiencia_ExperienciasPrestador_ExperienciaPrestadorId",
                        column: x => x.ExperienciaPrestadorId,
                        principalTable: "ExperienciasPrestador",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetenciaPortfolios",
                columns: table => new
                {
                    CompetenciaId = table.Column<int>(type: "int", nullable: false),
                    PortfolioPrestadorId = table.Column<int>(type: "int", nullable: false)
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
                        name: "FK_CompetenciaPortfolios_PortfoliosPrestador_PortfolioPrestadorId",
                        column: x => x.PortfolioPrestadorId,
                        principalTable: "PortfoliosPrestador",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContratosServico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjetoId = table.Column<int>(type: "int", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContratanteId = table.Column<int>(type: "int", nullable: false),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    ConteudoJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AssinadoContratanteEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IpContratante = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssinadoPrestadorEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IpPrestador = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConteudoHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ExpiraEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PdfKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EntregaRegistradaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CanceladoPorId = table.Column<int>(type: "int", nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                name: "ProjetoEmbeddings",
                columns: table => new
                {
                    ProjetoId = table.Column<int>(type: "int", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextoNormalizado = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModeloVersao = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "all-MiniLM-L6-v2"),
                    IndexadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjetoEmbeddings", x => x.ProjetoId);
                    table.ForeignKey(
                        name: "FK_ProjetoEmbeddings_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropostasProjeto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjetoId = table.Column<int>(type: "int", nullable: false),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VersaoAtual = table.Column<int>(type: "int", nullable: false),
                    ValidoAte = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CanceladoPorId = table.Column<int>(type: "int", nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropostasProjeto", x => x.Id);
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
                name: "Contratos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrestadorId = table.Column<int>(type: "int", nullable: true),
                    ContratanteId = table.Column<int>(type: "int", nullable: true),
                    ValorBruto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TaxaPlataforma = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PagoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IniciadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcluidoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrazoEntrega = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuantidadePosts = table.Column<int>(type: "int", nullable: true),
                    QuantidadeStories = table.Column<int>(type: "int", nullable: true),
                    QuantidadeReels = table.Column<int>(type: "int", nullable: true),
                    QuantidadeVideos = table.Column<int>(type: "int", nullable: true),
                    PlataformaPrincipal = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DiretrizesConteudo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExigeAprovacaoPrevia = table.Column<bool>(type: "bit", nullable: true),
                    PermiteUsoImagem = table.Column<bool>(type: "bit", nullable: true),
                    PrazoUsoImagemDias = table.Column<int>(type: "int", nullable: true),
                    ExclusividadeSegmento = table.Column<bool>(type: "bit", nullable: true),
                    MultaPorNaoEntrega = table.Column<bool>(type: "bit", nullable: true),
                    ValorMulta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contratos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contratos_Contratantes_ContratanteId",
                        column: x => x.ContratanteId,
                        principalTable: "Contratantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contratos_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contratos_Propostas_PropostaId",
                        column: x => x.PropostaId,
                        principalTable: "Propostas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PropostaNegociacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValorSugerido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PrazoEntregaSugerido = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Mensagem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Autor = table.Column<int>(type: "int", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropostaNegociacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropostaNegociacao_Propostas_PropostaId",
                        column: x => x.PropostaId,
                        principalTable: "Propostas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Avaliacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContratoServicoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AvaliadorId = table.Column<int>(type: "int", nullable: false),
                    AvaliadoId = table.Column<int>(type: "int", nullable: false),
                    Nota = table.Column<int>(type: "int", nullable: true),
                    Comentario = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Publica = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PublicadaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContratoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DadosContratante = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DadosPrestador = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConteudoFinal = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CongeladoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
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
                name: "PropostaVersoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    Objetivo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Escopo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Exclusoes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevisoesInclusas = table.Column<int>(type: "int", nullable: false),
                    PrazoTotal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValorTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Entrada = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FormaPagamento = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Observacoes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MarcosJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CriadoPor = table.Column<int>(type: "int", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
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
                name: "Conversas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContratanteId = table.Column<int>(type: "int", nullable: false),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContratoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EncerradaEm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversas_Contratantes_ContratanteId",
                        column: x => x.ContratanteId,
                        principalTable: "Contratantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversas_Contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "Contratos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Conversas_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversas_Propostas_PropostaId",
                        column: x => x.PropostaId,
                        principalTable: "Propostas",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Pagamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContratoServicoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContratoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValorBruto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxaPlataforma = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxaGateway = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PagoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LiberadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EstornadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LiberacaoAutomaticaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Metodo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Gateway = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GatewayPagamentoId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AsaasClienteId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AsaasTransferenciaId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StatusGateway = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PixQrCodePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PixQrCodeImagem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PixQrCodeExpiracao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayloadGateway = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                    table.ForeignKey(
                        name: "FK_Pagamentos_Contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "Contratos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MensagensProjeto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjetoId = table.Column<int>(type: "int", nullable: false),
                    RemetenteId = table.Column<int>(type: "int", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PropostaVersaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EnviadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
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

            migrationBuilder.CreateTable(
                name: "Mensagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemetenteId = table.Column<int>(type: "int", nullable: false),
                    Conteudo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EnviadaEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Lida = table.Column<bool>(type: "bit", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mensagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mensagens_Conversas_ConversaId",
                        column: x => x.ConversaId,
                        principalTable: "Conversas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Mensagens_Usuarios_RemetenteId",
                        column: x => x.RemetenteId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DisputasPagamento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PagamentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AbertoPorId = table.Column<int>(type: "int", nullable: false),
                    AbertoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenciasJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvidoPorId = table.Column<int>(type: "int", nullable: true),
                    ResolvidaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NotaResolucao = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PagamentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenciaExterna = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CriadoPorId = table.Column<int>(type: "int", nullable: false)
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
                name: "IX_ContaBancaria_PrestadorId",
                table: "ContaBancaria",
                column: "PrestadorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contratos_ContratanteId",
                table: "Contratos",
                column: "ContratanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Contratos_PlataformaPrincipal",
                table: "Contratos",
                column: "PlataformaPrincipal");

            migrationBuilder.CreateIndex(
                name: "IX_Contratos_PrestadorId",
                table: "Contratos",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Contratos_PropostaId",
                table: "Contratos",
                column: "PropostaId",
                unique: true,
                filter: "[PropostaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Contratos_Status",
                table: "Contratos",
                column: "Status");

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
                name: "IX_Conversas_ContratanteId",
                table: "Conversas",
                column: "ContratanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversas_ContratoId",
                table: "Conversas",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversas_PrestadorId",
                table: "Conversas",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversas_PropostaId",
                table: "Conversas",
                column: "PropostaId");

            migrationBuilder.CreateIndex(
                name: "IX_DisponibilidadesHorario_PrestadorId",
                table: "DisponibilidadesHorario",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputasPagamento_PagamentoId",
                table: "DisputasPagamento",
                column: "PagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienciasPrestador_PrestadorId",
                table: "ExperienciasPrestador",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerFinanceiro_PagamentoId",
                table: "LedgerFinanceiro",
                column: "PagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Mensagens_ConversaId",
                table: "Mensagens",
                column: "ConversaId");

            migrationBuilder.CreateIndex(
                name: "IX_Mensagens_RemetenteId",
                table: "Mensagens",
                column: "RemetenteId");

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_ProjetoId_EnviadoEm",
                table: "MensagensProjeto",
                columns: new[] { "ProjetoId", "EnviadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_PropostaVersaoId",
                table: "MensagensProjeto",
                column: "PropostaVersaoId");

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_RemetenteId",
                table: "MensagensProjeto",
                column: "RemetenteId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_ContratoId",
                table: "Pagamentos",
                column: "ContratoId");

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
                name: "IX_PrestadorEmbeddings_IndexadoEm",
                table: "PrestadorEmbeddings",
                column: "IndexadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_ProjetoEmbeddings_IndexadoEm",
                table: "ProjetoEmbeddings",
                column: "IndexadoEm");

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
                name: "IX_PropostaNegociacao_PropostaId",
                table: "PropostaNegociacao",
                column: "PropostaId");

            migrationBuilder.CreateIndex(
                name: "IX_Propostas_ContratanteId",
                table: "Propostas",
                column: "ContratanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Propostas_PrestadorId",
                table: "Propostas",
                column: "PrestadorId");

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
                name: "ContaBancaria");

            migrationBuilder.DropTable(
                name: "ContratoSnapshots");

            migrationBuilder.DropTable(
                name: "DisponibilidadesHorario");

            migrationBuilder.DropTable(
                name: "DisputasPagamento");

            migrationBuilder.DropTable(
                name: "LedgerFinanceiro");

            migrationBuilder.DropTable(
                name: "Mensagens");

            migrationBuilder.DropTable(
                name: "MensagensProjeto");

            migrationBuilder.DropTable(
                name: "PrestadorEmbeddings");

            migrationBuilder.DropTable(
                name: "ProjetoEmbeddings");

            migrationBuilder.DropTable(
                name: "PropostaNegociacao");

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
                name: "Pagamentos");

            migrationBuilder.DropTable(
                name: "Conversas");

            migrationBuilder.DropTable(
                name: "PropostaVersoes");

            migrationBuilder.DropTable(
                name: "ContratosServico");

            migrationBuilder.DropTable(
                name: "Contratos");

            migrationBuilder.DropTable(
                name: "PropostasProjeto");

            migrationBuilder.DropTable(
                name: "Propostas");

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
