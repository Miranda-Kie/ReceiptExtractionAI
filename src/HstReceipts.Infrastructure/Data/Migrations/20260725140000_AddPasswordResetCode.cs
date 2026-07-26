using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ReceiptDbContext))]
[Migration("20260725140000_AddPasswordResetCode")]
public partial class AddPasswordResetCode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Code",
            table: "PasswordResetTickets",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SentAtUtc",
            table: "PasswordResetTickets",
            type: "datetimeoffset",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Code",
            table: "PasswordResetTickets");

        migrationBuilder.DropColumn(
            name: "SentAtUtc",
            table: "PasswordResetTickets");
    }
}
