using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ReceiptDbContext))]
[Migration("20260724230000_AddEmailChangeChallenges")]
public partial class AddEmailChangeChallenges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EmailChangeChallenges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Token = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                NewEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                MaskedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedAtEst = table.Column<DateTime>(type: "datetime2", nullable: false),
                Consumed = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmailChangeChallenges", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EmailChangeChallenges_ExpiresAtUtc",
            table: "EmailChangeChallenges",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_EmailChangeChallenges_Token",
            table: "EmailChangeChallenges",
            column: "Token",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EmailChangeChallenges_UserId",
            table: "EmailChangeChallenges",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EmailChangeChallenges");
    }
}
