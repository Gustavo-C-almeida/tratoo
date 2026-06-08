using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaExcluidoEmUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExcluidoEm",
                table: "Usuarios",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcluidoEm",
                table: "Usuarios");
        }
    }
}
