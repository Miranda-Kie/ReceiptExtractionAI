using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceiptCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SimilarityKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ReceiptName = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    TransactionTime = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GstHst = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InitialStoreName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    InitialInvoiceNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InitialCurrency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceiptCorrections");
        }
    }
}
