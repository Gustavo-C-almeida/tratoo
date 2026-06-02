using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AjustesIA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrestadorEmbeddings");

            migrationBuilder.DropTable(
                name: "ProjetoEmbeddings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrestadorEmbeddings",
                columns: table => new
                {
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IndexadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ModeloVersao = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "all-MiniLM-L6-v2"),
                    TextoNormalizado = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                name: "ProjetoEmbeddings",
                columns: table => new
                {
                    ProjetoId = table.Column<int>(type: "int", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IndexadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ModeloVersao = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "all-MiniLM-L6-v2"),
                    TextoNormalizado = table.Column<string>(type: "nvarchar(max)", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_PrestadorEmbeddings_IndexadoEm",
                table: "PrestadorEmbeddings",
                column: "IndexadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_ProjetoEmbeddings_IndexadoEm",
                table: "ProjetoEmbeddings",
                column: "IndexadoEm");
        }
    }
}
