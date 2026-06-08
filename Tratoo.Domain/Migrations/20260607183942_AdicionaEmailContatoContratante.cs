using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaEmailContatoContratante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumFreelancersDesejados",
                table: "Projetos");

            migrationBuilder.DropColumn(
                name: "GitHubUrl",
                table: "Prestadores");

            migrationBuilder.DropColumn(
                name: "ValorHora",
                table: "Prestadores");

            migrationBuilder.AddColumn<string>(
                name: "EmailContato",
                table: "Contratantes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArquivoUrl",
                table: "CertificacoesPrestador",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailContato",
                table: "Contratantes");

            migrationBuilder.DropColumn(
                name: "ArquivoUrl",
                table: "CertificacoesPrestador");

            migrationBuilder.AddColumn<int>(
                name: "NumFreelancersDesejados",
                table: "Projetos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GitHubUrl",
                table: "Prestadores",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorHora",
                table: "Prestadores",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }
    }
}
