using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrectionInitialAmountSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InitialGstHst",
                table: "ReceiptCorrections",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InitialReceiptDate",
                table: "ReceiptCorrections",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InitialSubtotal",
                table: "ReceiptCorrections",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InitialTotalAmount",
                table: "ReceiptCorrections",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InitialTransactionTime",
                table: "ReceiptCorrections",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialGstHst",
                table: "ReceiptCorrections");

            migrationBuilder.DropColumn(
                name: "InitialReceiptDate",
                table: "ReceiptCorrections");

            migrationBuilder.DropColumn(
                name: "InitialSubtotal",
                table: "ReceiptCorrections");

            migrationBuilder.DropColumn(
                name: "InitialTotalAmount",
                table: "ReceiptCorrections");

            migrationBuilder.DropColumn(
                name: "InitialTransactionTime",
                table: "ReceiptCorrections");
        }
    }
}
