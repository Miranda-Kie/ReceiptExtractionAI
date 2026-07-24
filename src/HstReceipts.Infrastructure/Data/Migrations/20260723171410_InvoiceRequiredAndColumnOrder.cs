using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HstReceipts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceRequiredAndColumnOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [Receipts] SET [InvoiceNumber] = N'' WHERE [InvoiceNumber] IS NULL;

ALTER TABLE [ReceiptCorrections] DROP CONSTRAINT [FK_ReceiptCorrections_Receipts_ReceiptId];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Receipts_BatchId' AND object_id = OBJECT_ID(N'[Receipts]'))
    DROP INDEX [IX_Receipts_BatchId] ON [Receipts];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Receipts_InvoiceNumber_ReceiptDate' AND object_id = OBJECT_ID(N'[Receipts]'))
    DROP INDEX [IX_Receipts_InvoiceNumber_ReceiptDate] ON [Receipts];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Receipts_StoreName_InvoiceNumber' AND object_id = OBJECT_ID(N'[Receipts]'))
    DROP INDEX [IX_Receipts_StoreName_InvoiceNumber] ON [Receipts];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Receipts_StoreName_ReceiptDate_TotalAmount' AND object_id = OBJECT_ID(N'[Receipts]'))
    DROP INDEX [IX_Receipts_StoreName_ReceiptDate_TotalAmount] ON [Receipts];

ALTER TABLE [Receipts] DROP CONSTRAINT [PK_Receipts];
EXEC sp_rename N'[Receipts]', N'Receipts_Old';

CREATE TABLE [Receipts] (
    [Id] uniqueidentifier NOT NULL,
    [InvoiceNumber] nvarchar(128) NOT NULL,
    [StoreName] nvarchar(256) NOT NULL,
    [Currency] nvarchar(8) NOT NULL,
    [Subtotal] decimal(18,2) NOT NULL,
    [GstHst] decimal(18,2) NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [ReceiptDate] date NOT NULL,
    [TransactionTime] nvarchar(64) NULL,
    [MatchStatus] nvarchar(16) NOT NULL CONSTRAINT [DF_Receipts_MatchStatus] DEFAULT N'New',
    [BatchId] uniqueidentifier NOT NULL,
    [CreatedAtEst] datetime2 NOT NULL,
    [ModifiedAtEst] datetime2 NULL,
    CONSTRAINT [PK_Receipts] PRIMARY KEY ([Id])
);

INSERT INTO [Receipts] (
    [Id], [InvoiceNumber], [StoreName], [Currency], [Subtotal], [GstHst], [TotalAmount],
    [ReceiptDate], [TransactionTime], [MatchStatus], [BatchId], [CreatedAtEst], [ModifiedAtEst])
SELECT
    [Id], [InvoiceNumber], [StoreName], [Currency], [Subtotal], [GstHst], [TotalAmount],
    [ReceiptDate], [TransactionTime], [MatchStatus], [BatchId], [CreatedAtEst], [ModifiedAtEst]
FROM [Receipts_Old];

DROP TABLE [Receipts_Old];

CREATE INDEX [IX_Receipts_BatchId] ON [Receipts] ([BatchId]);
CREATE INDEX [IX_Receipts_InvoiceNumber] ON [Receipts] ([InvoiceNumber]);
CREATE INDEX [IX_Receipts_InvoiceNumber_StoreName] ON [Receipts] ([InvoiceNumber], [StoreName]);

ALTER TABLE [ReceiptCorrections] WITH CHECK ADD CONSTRAINT [FK_ReceiptCorrections_Receipts_ReceiptId]
    FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE CASCADE;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE [ReceiptCorrections] DROP CONSTRAINT [FK_ReceiptCorrections_Receipts_ReceiptId];

DROP INDEX [IX_Receipts_BatchId] ON [Receipts];
DROP INDEX [IX_Receipts_InvoiceNumber] ON [Receipts];
DROP INDEX [IX_Receipts_InvoiceNumber_StoreName] ON [Receipts];

ALTER TABLE [Receipts] DROP CONSTRAINT [PK_Receipts];
IF OBJECT_ID(N'[DF_Receipts_MatchStatus]', N'D') IS NOT NULL
    ALTER TABLE [Receipts] DROP CONSTRAINT [DF_Receipts_MatchStatus];
EXEC sp_rename N'[Receipts]', N'Receipts_New';

CREATE TABLE [Receipts] (
    [Id] uniqueidentifier NOT NULL,
    [StoreName] nvarchar(256) NOT NULL,
    [InvoiceNumber] nvarchar(128) NULL,
    [Currency] nvarchar(8) NOT NULL,
    [Subtotal] decimal(18,2) NOT NULL,
    [GstHst] decimal(18,2) NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [ReceiptDate] date NOT NULL,
    [TransactionTime] nvarchar(64) NULL,
    [MatchStatus] nvarchar(16) NOT NULL CONSTRAINT [DF_Receipts_MatchStatus] DEFAULT N'New',
    [BatchId] uniqueidentifier NOT NULL,
    [CreatedAtEst] datetime2 NOT NULL,
    [ModifiedAtEst] datetime2 NULL,
    CONSTRAINT [PK_Receipts] PRIMARY KEY ([Id])
);

INSERT INTO [Receipts] (
    [Id], [StoreName], [InvoiceNumber], [Currency], [Subtotal], [GstHst], [TotalAmount],
    [ReceiptDate], [TransactionTime], [MatchStatus], [BatchId], [CreatedAtEst], [ModifiedAtEst])
SELECT
    [Id], [StoreName], [InvoiceNumber], [Currency], [Subtotal], [GstHst], [TotalAmount],
    [ReceiptDate], [TransactionTime], [MatchStatus], [BatchId], [CreatedAtEst], [ModifiedAtEst]
FROM [Receipts_New];

DROP TABLE [Receipts_New];

CREATE INDEX [IX_Receipts_BatchId] ON [Receipts] ([BatchId]);
CREATE INDEX [IX_Receipts_InvoiceNumber_ReceiptDate] ON [Receipts] ([InvoiceNumber], [ReceiptDate]);
CREATE INDEX [IX_Receipts_StoreName_InvoiceNumber] ON [Receipts] ([StoreName], [InvoiceNumber]);
CREATE INDEX [IX_Receipts_StoreName_ReceiptDate_TotalAmount] ON [Receipts] ([StoreName], [ReceiptDate], [TotalAmount]);

ALTER TABLE [ReceiptCorrections] WITH CHECK ADD CONSTRAINT [FK_ReceiptCorrections_Receipts_ReceiptId]
    FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE CASCADE;
");
        }
    }
}
