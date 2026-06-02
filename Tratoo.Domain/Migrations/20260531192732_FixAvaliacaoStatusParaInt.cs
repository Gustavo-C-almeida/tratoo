using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class FixAvaliacaoStatusParaInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Normaliza qualquer valor string legível para o inteiro equivalente
            // antes de alterar o tipo da coluna.
            migrationBuilder.Sql(@"
                UPDATE Avaliacoes SET Status = '0' WHERE Status = 'Pendente';
                UPDATE Avaliacoes SET Status = '1' WHERE Status = 'Publicada';
                UPDATE Avaliacoes SET Status = '2' WHERE Status = 'Oculta';
            ");

            // Remove o índice existente (necessário antes de ALTER COLUMN)
            migrationBuilder.DropIndex(
                name: "IX_Avaliacoes_Status",
                table: "Avaliacoes");

            // Converte a coluna de nvarchar(450) para int
            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Avaliacoes",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            // Recria o índice com o tipo correto
            migrationBuilder.CreateIndex(
                name: "IX_Avaliacoes_Status",
                table: "Avaliacoes",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Avaliacoes_Status",
                table: "Avaliacoes");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Avaliacoes",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.Sql(@"
                UPDATE Avaliacoes SET Status = 'Pendente'  WHERE Status = '0';
                UPDATE Avaliacoes SET Status = 'Publicada' WHERE Status = '1';
                UPDATE Avaliacoes SET Status = 'Oculta'    WHERE Status = '2';
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Avaliacoes_Status",
                table: "Avaliacoes",
                column: "Status");
        }
    }
}
