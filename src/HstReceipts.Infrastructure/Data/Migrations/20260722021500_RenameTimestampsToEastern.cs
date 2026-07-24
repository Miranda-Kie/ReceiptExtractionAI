using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

[DbContext(typeof(ReceiptDbContext))]
[Migration("20260722021500_RenameTimestampsToEastern")]
public partial class RenameTimestampsToEastern : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Receipts: CreatedAtUtc -> CreatedAtEst, add ModifiedAtEst (convert UTC values to Eastern).
        migrationBuilder.RenameColumn(
            name: "CreatedAtUtc",
            table: "Receipts",
            newName: "CreatedAtEst");

        migrationBuilder.Sql("""
            UPDATE [Receipts]
            SET [CreatedAtEst] = CAST(([CreatedAtEst] AT TIME ZONE 'UTC') AT TIME ZONE 'Eastern Standard Time' AS datetime2);
            """);

        migrationBuilder.AddColumn<DateTime>(
            name: "ModifiedAtEst",
            table: "Receipts",
            type: "datetime2",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.Sql("""
            UPDATE [Receipts] SET [ModifiedAtEst] = [CreatedAtEst];
            """);

        // Users
        migrationBuilder.RenameColumn(
            name: "CreatedAtUtc",
            table: "Users",
            newName: "CreatedAtEst");

        migrationBuilder.Sql("""
            UPDATE [Users]
            SET [CreatedAtEst] = CAST(([CreatedAtEst] AT TIME ZONE 'UTC') AT TIME ZONE 'Eastern Standard Time' AS datetime2);
            """);

        migrationBuilder.AddColumn<DateTime>(
            name: "ModifiedAtEst",
            table: "Users",
            type: "datetime2",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.Sql("""
            UPDATE [Users] SET [ModifiedAtEst] = [CreatedAtEst];
            """);

        // ReceiptAiProfiles: UpdatedAtUtc -> ModifiedAtEst
        migrationBuilder.RenameColumn(
            name: "UpdatedAtUtc",
            table: "ReceiptAiProfiles",
            newName: "ModifiedAtEst");

        migrationBuilder.Sql("""
            UPDATE [ReceiptAiProfiles]
            SET [ModifiedAtEst] = CAST(([ModifiedAtEst] AT TIME ZONE 'UTC') AT TIME ZONE 'Eastern Standard Time' AS datetime2);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE [ReceiptAiProfiles]
            SET [ModifiedAtEst] = CAST(([ModifiedAtEst] AT TIME ZONE 'Eastern Standard Time') AT TIME ZONE 'UTC' AS datetime2);
            """);

        migrationBuilder.RenameColumn(
            name: "ModifiedAtEst",
            table: "ReceiptAiProfiles",
            newName: "UpdatedAtUtc");

        migrationBuilder.DropColumn(
            name: "ModifiedAtEst",
            table: "Users");

        migrationBuilder.Sql("""
            UPDATE [Users]
            SET [CreatedAtEst] = CAST(([CreatedAtEst] AT TIME ZONE 'Eastern Standard Time') AT TIME ZONE 'UTC' AS datetime2);
            """);

        migrationBuilder.RenameColumn(
            name: "CreatedAtEst",
            table: "Users",
            newName: "CreatedAtUtc");

        migrationBuilder.DropColumn(
            name: "ModifiedAtEst",
            table: "Receipts");

        migrationBuilder.Sql("""
            UPDATE [Receipts]
            SET [CreatedAtEst] = CAST(([CreatedAtEst] AT TIME ZONE 'Eastern Standard Time') AT TIME ZONE 'UTC' AS datetime2);
            """);

        migrationBuilder.RenameColumn(
            name: "CreatedAtEst",
            table: "Receipts",
            newName: "CreatedAtUtc");
    }
}
