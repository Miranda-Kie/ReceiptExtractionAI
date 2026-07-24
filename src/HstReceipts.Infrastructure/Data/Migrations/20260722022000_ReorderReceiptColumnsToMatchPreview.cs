using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations;

/// <summary>
/// Rebuilds Receipts so physical column order matches the preview table.
/// </summary>
[DbContext(typeof(ReceiptDbContext))]
[Migration("20260722022000_ReorderReceiptColumnsToMatchPreview")]
public partial class ReorderReceiptColumnsToMatchPreview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            BEGIN TRANSACTION;

            CREATE TABLE [Receipts_new] (
                [Id] uniqueidentifier NOT NULL,
                [StoreName] nvarchar(256) NOT NULL,
                [InvoiceNumber] nvarchar(128) NULL,
                [Currency] nvarchar(8) NULL,
                [Subtotal] decimal(18,2) NOT NULL,
                [GstHst] decimal(18,2) NOT NULL,
                [TotalAmount] decimal(18,2) NOT NULL,
                [ReceiptDate] date NULL,
                [TransactionTime] nvarchar(64) NULL,
                [BatchId] uniqueidentifier NOT NULL,
                [CreatedAtEst] datetime2 NOT NULL,
                [ModifiedAtEst] datetime2 NOT NULL,
                CONSTRAINT [PK_Receipts_new] PRIMARY KEY ([Id])
            );

            INSERT INTO [Receipts_new] (
                [Id], [StoreName], [InvoiceNumber], [Currency], [Subtotal], [GstHst],
                [TotalAmount], [ReceiptDate], [TransactionTime], [BatchId],
                [CreatedAtEst], [ModifiedAtEst])
            SELECT
                [Id], [StoreName], [InvoiceNumber], [Currency], [Subtotal], [GstHst],
                [TotalAmount], [ReceiptDate], [TransactionTime], [BatchId],
                [CreatedAtEst], [ModifiedAtEst]
            FROM [Receipts];

            DROP TABLE [Receipts];

            EXEC sp_rename N'Receipts_new', N'Receipts';
            EXEC sp_rename N'PK_Receipts_new', N'PK_Receipts', N'OBJECT';

            CREATE INDEX [IX_Receipts_BatchId] ON [Receipts] ([BatchId]);
            CREATE INDEX [IX_Receipts_StoreName_InvoiceNumber] ON [Receipts] ([StoreName], [InvoiceNumber]);

            COMMIT;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Previous physical order is not reconstructed; data is preserved with current columns.
        migrationBuilder.Sql("""
            BEGIN TRANSACTION;

            CREATE TABLE [Receipts_old] (
                [Id] uniqueidentifier NOT NULL,
                [BatchId] uniqueidentifier NOT NULL,
                [CreatedAtEst] datetime2 NOT NULL,
                [Currency] nvarchar(8) NULL,
                [GstHst] decimal(18,2) NOT NULL,
                [InvoiceNumber] nvarchar(128) NULL,
                [ModifiedAtEst] datetime2 NOT NULL,
                [ReceiptDate] date NULL,
                [StoreName] nvarchar(256) NOT NULL,
                [Subtotal] decimal(18,2) NOT NULL,
                [TotalAmount] decimal(18,2) NOT NULL,
                [TransactionTime] nvarchar(64) NULL,
                CONSTRAINT [PK_Receipts_old] PRIMARY KEY ([Id])
            );

            INSERT INTO [Receipts_old] (
                [Id], [BatchId], [CreatedAtEst], [Currency], [GstHst], [InvoiceNumber],
                [ModifiedAtEst], [ReceiptDate], [StoreName], [Subtotal], [TotalAmount], [TransactionTime])
            SELECT
                [Id], [BatchId], [CreatedAtEst], [Currency], [GstHst], [InvoiceNumber],
                [ModifiedAtEst], [ReceiptDate], [StoreName], [Subtotal], [TotalAmount], [TransactionTime]
            FROM [Receipts];

            DROP TABLE [Receipts];

            EXEC sp_rename N'Receipts_old', N'Receipts';
            EXEC sp_rename N'PK_Receipts_old', N'PK_Receipts', N'OBJECT';

            CREATE INDEX [IX_Receipts_BatchId] ON [Receipts] ([BatchId]);
            CREATE INDEX [IX_Receipts_StoreName_InvoiceNumber] ON [Receipts] ([StoreName], [InvoiceNumber]);

            COMMIT;
            """);
    }
}
