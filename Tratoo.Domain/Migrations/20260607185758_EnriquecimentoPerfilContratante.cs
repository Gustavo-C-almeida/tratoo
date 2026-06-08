using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class EnriquecimentoPerfilContratante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Disponibilidade",
                table: "Contratantes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdiomasAceitosJson",
                table: "Contratantes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PorQueTrabalharComigo",
                table: "Contratantes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TamanhoEquipe",
                table: "Contratantes",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disponibilidade",
                table: "Contratantes");

            migrationBuilder.DropColumn(
                name: "IdiomasAceitosJson",
                table: "Contratantes");

            migrationBuilder.DropColumn(
                name: "PorQueTrabalharComigo",
                table: "Contratantes");

            migrationBuilder.DropColumn(
                name: "TamanhoEquipe",
                table: "Contratantes");
        }
    }
}
