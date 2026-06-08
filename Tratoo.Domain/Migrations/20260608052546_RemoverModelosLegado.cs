using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class RemoverModelosLegado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pagamentos_Contratos_ContratoId",
                table: "Pagamentos");

            migrationBuilder.DropTable(
                name: "Mensagens");

            migrationBuilder.DropTable(
                name: "PropostaNegociacao");

            migrationBuilder.DropTable(
                name: "Conversas");

            migrationBuilder.DropTable(
                name: "Contratos");

            migrationBuilder.DropTable(
                name: "Propostas");

            migrationBuilder.DropIndex(
                name: "IX_Pagamentos_ContratoId",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "ContratoId",
                table: "Pagamentos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContratoId",
                table: "Pagamentos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Propostas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContratanteId = table.Column<int>(type: "int", nullable: false),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false),
                    AtualizadaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CondicoesGerais = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContratoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataCancelamento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DireitosUsoEntregavel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DireitosUsoImagem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DuracaoMeses = table.Column<int>(type: "int", nullable: true),
                    Entregaveis = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HorasEstimadas = table.Column<int>(type: "int", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MotivoRecusa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PrazoEntrega = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrazoResposta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RespondidaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TipoCobranca = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorHora = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ValorMensal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ValorTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Versao = table.Column<int>(type: "int", nullable: false)
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
                name: "Contratos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContratanteId = table.Column<int>(type: "int", nullable: true),
                    PrestadorId = table.Column<int>(type: "int", nullable: true),
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcluidoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DiretrizesConteudo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExclusividadeSegmento = table.Column<bool>(type: "bit", nullable: true),
                    ExigeAprovacaoPrevia = table.Column<bool>(type: "bit", nullable: true),
                    IniciadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MultaPorNaoEntrega = table.Column<bool>(type: "bit", nullable: true),
                    PagoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PermiteUsoImagem = table.Column<bool>(type: "bit", nullable: true),
                    PlataformaPrincipal = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PrazoEntrega = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrazoUsoImagemDias = table.Column<int>(type: "int", nullable: true),
                    QuantidadePosts = table.Column<int>(type: "int", nullable: true),
                    QuantidadeReels = table.Column<int>(type: "int", nullable: true),
                    QuantidadeStories = table.Column<int>(type: "int", nullable: true),
                    QuantidadeVideos = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ValorBruto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
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
                    Autor = table.Column<int>(type: "int", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Mensagem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrazoEntregaSugerido = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValorSugerido = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
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
                name: "Conversas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContratanteId = table.Column<int>(type: "int", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    PropostaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_ContratoId",
                table: "Pagamentos",
                column: "ContratoId");

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
                name: "IX_Mensagens_ConversaId",
                table: "Mensagens",
                column: "ConversaId");

            migrationBuilder.CreateIndex(
                name: "IX_Mensagens_RemetenteId",
                table: "Mensagens",
                column: "RemetenteId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_Pagamentos_Contratos_ContratoId",
                table: "Pagamentos",
                column: "ContratoId",
                principalTable: "Contratos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
