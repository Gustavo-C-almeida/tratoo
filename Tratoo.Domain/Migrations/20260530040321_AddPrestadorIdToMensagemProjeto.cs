using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddPrestadorIdToMensagemProjeto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MensagensProjeto_ProjetoId_EnviadoEm",
                table: "MensagensProjeto");

            migrationBuilder.AddColumn<int>(
                name: "PrestadorId",
                table: "MensagensProjeto",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_PrestadorId_EnviadoEm",
                table: "MensagensProjeto",
                columns: new[] { "PrestadorId", "EnviadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_ProjetoId_PrestadorId_EnviadoEm",
                table: "MensagensProjeto",
                columns: new[] { "ProjetoId", "PrestadorId", "EnviadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MensagensProjeto_PrestadorId_EnviadoEm",
                table: "MensagensProjeto");

            migrationBuilder.DropIndex(
                name: "IX_MensagensProjeto_ProjetoId_PrestadorId_EnviadoEm",
                table: "MensagensProjeto");

            migrationBuilder.DropColumn(
                name: "PrestadorId",
                table: "MensagensProjeto");

            migrationBuilder.CreateIndex(
                name: "IX_MensagensProjeto_ProjetoId_EnviadoEm",
                table: "MensagensProjeto",
                columns: new[] { "ProjetoId", "EnviadoEm" });
        }
    }
}
