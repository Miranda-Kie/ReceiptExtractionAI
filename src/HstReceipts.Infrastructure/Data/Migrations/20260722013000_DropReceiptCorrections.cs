using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

[DbContext(typeof(ReceiptDbContext))]
[Migration("20260722013000_DropReceiptCorrections")]
public partial class DropReceiptCorrections : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ReceiptCorrections");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReceiptCorrections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                GstHst = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                InitialCurrency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                InitialGstHst = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                InitialInvoiceNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                InitialReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                InitialStoreName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                InitialSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                InitialTotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                InitialTransactionTime = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                InvoiceNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                ReceiptName = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                SimilarityKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                StoreName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                TransactionTime = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReceiptCorrections", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReceiptCorrections_CapturedAtUtc",
            table: "ReceiptCorrections",
            column: "CapturedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_ReceiptCorrections_SimilarityKey",
            table: "ReceiptCorrections",
            column: "SimilarityKey");
    }
}
