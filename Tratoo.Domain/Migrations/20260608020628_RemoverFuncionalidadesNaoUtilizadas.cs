using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tratoo.Domain.Migrations
{
    /// <inheritdoc />
    public partial class RemoverFuncionalidadesNaoUtilizadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatConviteMensagens");

            migrationBuilder.DropTable(
                name: "ChatsConvite");

            migrationBuilder.DropColumn(
                name: "RespostaPublica",
                table: "Avaliacoes");

            migrationBuilder.DropColumn(
                name: "RespostaPublicaCriadaEm",
                table: "Avaliacoes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "ChatsConvite",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContratanteId = table.Column<int>(type: "int", nullable: false),
                    ConviteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrestadorId = table.Column<int>(type: "int", nullable: false),
                    ProjetoId = table.Column<int>(type: "int", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatsConvite", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatsConvite_Contratantes_ContratanteId",
                        column: x => x.ContratanteId,
                        principalTable: "Contratantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatsConvite_ConvitesProjeto_ConviteId",
                        column: x => x.ConviteId,
                        principalTable: "ConvitesProjeto",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatsConvite_Prestadores_PrestadorId",
                        column: x => x.PrestadorId,
                        principalTable: "Prestadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatsConvite_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatConviteMensagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemetenteId = table.Column<int>(type: "int", nullable: false),
                    EnviadoEm = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Texto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConviteMensagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatConviteMensagens_ChatsConvite_ChatId",
                        column: x => x.ChatId,
                        principalTable: "ChatsConvite",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatConviteMensagens_Usuarios_RemetenteId",
                        column: x => x.RemetenteId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConviteMensagens_ChatId_EnviadoEm",
                table: "ChatConviteMensagens",
                columns: new[] { "ChatId", "EnviadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConviteMensagens_RemetenteId",
                table: "ChatConviteMensagens",
                column: "RemetenteId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatsConvite_ContratanteId_PrestadorId",
                table: "ChatsConvite",
                columns: new[] { "ContratanteId", "PrestadorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatsConvite_ConviteId",
                table: "ChatsConvite",
                column: "ConviteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatsConvite_PrestadorId",
                table: "ChatsConvite",
                column: "PrestadorId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatsConvite_ProjetoId",
                table: "ChatsConvite",
                column: "ProjetoId");
        }
    }
}
