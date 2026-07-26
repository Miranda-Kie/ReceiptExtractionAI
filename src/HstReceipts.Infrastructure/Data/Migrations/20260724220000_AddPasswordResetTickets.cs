using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ReceiptDbContext))]
[Migration("20260724220000_AddPasswordResetTickets")]
public partial class AddPasswordResetTickets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PasswordResetTickets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Token = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                MaskedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedAtEst = table.Column<DateTime>(type: "datetime2", nullable: false),
                Consumed = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PasswordResetTickets", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetTickets_ExpiresAtUtc",
            table: "PasswordResetTickets",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetTickets_Token",
            table: "PasswordResetTickets",
            column: "Token",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetTickets_UserId",
            table: "PasswordResetTickets",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PasswordResetTickets");
    }
}
