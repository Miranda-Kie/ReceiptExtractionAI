using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ReceiptDbContext))]
[Migration("20260725150000_UniqueUserEmail")]
public partial class UniqueUserEmail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Users_Email",
            table: "Users",
            column: "Email",
            unique: true,
            filter: "[Email] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_Email",
            table: "Users");
    }
}
