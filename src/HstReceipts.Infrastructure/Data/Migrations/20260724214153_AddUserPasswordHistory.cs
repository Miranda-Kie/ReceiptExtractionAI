using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPasswordHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedAtEst = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPasswordHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPasswordHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPasswordHistories_UserId",
                table: "UserPasswordHistories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPasswordHistories_UserId_CreatedAtEst",
                table: "UserPasswordHistories",
                columns: new[] { "UserId", "CreatedAtEst" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPasswordHistories");
        }
    }
}
