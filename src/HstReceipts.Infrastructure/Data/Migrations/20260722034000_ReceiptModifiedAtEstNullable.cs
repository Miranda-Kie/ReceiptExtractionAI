using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

[DbContext(typeof(ReceiptDbContext))]
[Migration("20260722034000_ReceiptModifiedAtEstNullable")]
public partial class ReceiptModifiedAtEstNullable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAtEst",
            table: "Receipts",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE [Receipts] SET [ModifiedAtEst] = [CreatedAtEst] WHERE [ModifiedAtEst] IS NULL;
            """);

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAtEst",
            table: "Receipts",
            type: "datetime2",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);
    }
}
