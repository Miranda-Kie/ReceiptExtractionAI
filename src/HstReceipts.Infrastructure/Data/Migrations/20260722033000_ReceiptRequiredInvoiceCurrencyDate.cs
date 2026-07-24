using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

[DbContext(typeof(ReceiptDbContext))]
[Migration("20260722033000_ReceiptRequiredInvoiceCurrencyDate")]
public partial class ReceiptRequiredInvoiceCurrencyDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Receipts_StoreName_InvoiceNumber",
            table: "Receipts");

        migrationBuilder.Sql("""
            UPDATE [Receipts] SET [InvoiceNumber] = N'UNKNOWN' WHERE [InvoiceNumber] IS NULL OR LTRIM(RTRIM([InvoiceNumber])) = N'';
            UPDATE [Receipts] SET [Currency] = N'CAD' WHERE [Currency] IS NULL OR LTRIM(RTRIM([Currency])) = N'';
            UPDATE [Receipts] SET [ReceiptDate] = '1900-01-01' WHERE [ReceiptDate] IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "InvoiceNumber",
            table: "Receipts",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Currency",
            table: "Receipts",
            type: "nvarchar(8)",
            maxLength: 8,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(8)",
            oldMaxLength: 8,
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "ReceiptDate",
            table: "Receipts",
            type: "date",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "date",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Receipts_StoreName_InvoiceNumber",
            table: "Receipts",
            columns: new[] { "StoreName", "InvoiceNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Receipts_StoreName_InvoiceNumber",
            table: "Receipts");

        migrationBuilder.AlterColumn<string>(
            name: "InvoiceNumber",
            table: "Receipts",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "Currency",
            table: "Receipts",
            type: "nvarchar(8)",
            maxLength: 8,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(8)",
            oldMaxLength: 8);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "ReceiptDate",
            table: "Receipts",
            type: "date",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "date");

        migrationBuilder.CreateIndex(
            name: "IX_Receipts_StoreName_InvoiceNumber",
            table: "Receipts",
            columns: new[] { "StoreName", "InvoiceNumber" });
    }
}
