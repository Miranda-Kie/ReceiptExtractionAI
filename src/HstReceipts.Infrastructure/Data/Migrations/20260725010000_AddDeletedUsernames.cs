using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ReceiptDbContext))]
[Migration("20260725010000_AddDeletedUsernames")]
public partial class AddDeletedUsernames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DeletedUsernames",
            columns: table => new
            {
                Username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DeletedAtEst = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeletedUsernames", x => x.Username);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DeletedUsernames");
    }
}
