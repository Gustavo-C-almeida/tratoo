using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddConviteProjeto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConvitesProjeto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjetoId = table.Column<int>(type: "int", nullable: false),
                    ContratanteId = table.Column<int>(type: "int", nullable: false),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    MensagemInicial = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OrcamentoSugerido = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PrazoDesejado = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    RespondidoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoRecusa = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConvitesProjeto");
        }
    }
}
