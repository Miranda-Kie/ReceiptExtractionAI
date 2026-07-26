using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessingBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TotalFiles = table.Column<int>(type: "int", nullable: false),
                    CompletedFiles = table.Column<int>(type: "int", nullable: false),
                    FailedFiles = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtEst = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtEst = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingBatchResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ReceiptName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    TransactionTime = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GstHst = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceTextPreview = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WarningsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtEst = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingBatchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingBatchResults_ProcessingBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ProcessingBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingBatches_CreatedAtEst",
                table: "ProcessingBatches",
                column: "CreatedAtEst");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingBatches_Username",
                table: "ProcessingBatches",
                column: "Username");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingBatchResults_BatchId",
                table: "ProcessingBatchResults",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessingBatchResults");

            migrationBuilder.DropTable(
                name: "ProcessingBatches");
        }
    }
}
