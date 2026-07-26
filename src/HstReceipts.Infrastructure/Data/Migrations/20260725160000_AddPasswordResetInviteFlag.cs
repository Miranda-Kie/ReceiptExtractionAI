using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ReceiptDbContext))]
[Migration("20260725160000_AddPasswordResetInviteFlag")]
public partial class AddPasswordResetInviteFlag : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsSetPasswordInvite",
            table: "PasswordResetTickets",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsSetPasswordInvite",
            table: "PasswordResetTickets");
    }
}
