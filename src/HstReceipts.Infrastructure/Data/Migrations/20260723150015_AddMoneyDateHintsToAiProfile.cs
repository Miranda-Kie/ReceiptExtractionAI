using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyDateHintsToAiProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GstHstHint",
                table: "ReceiptAiProfiles",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptDateHint",
                table: "ReceiptAiProfiles",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtotalHint",
                table: "ReceiptAiProfiles",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotalAmountHint",
                table: "ReceiptAiProfiles",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GstHstHint",
                table: "ReceiptAiProfiles");

            migrationBuilder.DropColumn(
                name: "ReceiptDateHint",
                table: "ReceiptAiProfiles");

            migrationBuilder.DropColumn(
                name: "SubtotalHint",
                table: "ReceiptAiProfiles");

            migrationBuilder.DropColumn(
                name: "TotalAmountHint",
                table: "ReceiptAiProfiles");
        }
    }
}
