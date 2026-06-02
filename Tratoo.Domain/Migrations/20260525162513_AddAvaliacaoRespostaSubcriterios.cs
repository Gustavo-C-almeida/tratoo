using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddAvaliacaoRespostaSubcriterios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "NotaComunicacao",
                table: "Avaliacoes",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "NotaPrazo",
                table: "Avaliacoes",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "NotaQualidade",
                table: "Avaliacoes",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RespostaPublica",
                table: "Avaliacoes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespostaPublicaCriadaEm",
                table: "Avaliacoes",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotaComunicacao",
                table: "Avaliacoes");

            migrationBuilder.DropColumn(
                name: "NotaPrazo",
                table: "Avaliacoes");

            migrationBuilder.DropColumn(
                name: "NotaQualidade",
                table: "Avaliacoes");

            migrationBuilder.DropColumn(
                name: "RespostaPublica",
                table: "Avaliacoes");

            migrationBuilder.DropColumn(
                name: "RespostaPublicaCriadaEm",
                table: "Avaliacoes");
        }
    }
}
