using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SoftMatchAndReceiptCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "ModifiedAtEst",
                table: "Receipts",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 12)
                .OldAnnotation("Relational:ColumnOrder", 11);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceNumber",
                table: "Receipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtEst",
                table: "Receipts",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2")
                .Annotation("Relational:ColumnOrder", 11)
                .OldAnnotation("Relational:ColumnOrder", 10);

            migrationBuilder.AlterColumn<Guid>(
                name: "BatchId",
                table: "Receipts",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier")
                .Annotation("Relational:ColumnOrder", 10)
                .OldAnnotation("Relational:ColumnOrder", 9);

            migrationBuilder.AddColumn<string>(
                name: "MatchStatus",
                table: "Receipts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "New")
                .Annotation("Relational:ColumnOrder", 9);

            migrationBuilder.CreateTable(
                name: "ReceiptCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    MatchKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtEst = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptCorrections_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_InvoiceNumber_ReceiptDate",
                table: "Receipts",
                columns: new[] { "InvoiceNumber", "ReceiptDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_StoreName_ReceiptDate_TotalAmount",
                table: "Receipts",
                columns: new[] { "StoreName", "ReceiptDate", "TotalAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrections_BatchId",
                table: "ReceiptCorrections",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrections_CreatedAtEst",
                table: "ReceiptCorrections",
                column: "CreatedAtEst");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrections_ReceiptId",
                table: "ReceiptCorrections",
                column: "ReceiptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceiptCorrections");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_InvoiceNumber_ReceiptDate",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_StoreName_ReceiptDate_TotalAmount",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "MatchStatus",
                table: "Receipts");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ModifiedAtEst",
                table: "Receipts",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 11)
                .OldAnnotation("Relational:ColumnOrder", 12);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceNumber",
                table: "Receipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtEst",
                table: "Receipts",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2")
                .Annotation("Relational:ColumnOrder", 10)
                .OldAnnotation("Relational:ColumnOrder", 11);

            migrationBuilder.AlterColumn<Guid>(
                name: "BatchId",
                table: "Receipts",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier")
                .Annotation("Relational:ColumnOrder", 9)
                .OldAnnotation("Relational:ColumnOrder", 10);
        }
    }
}
