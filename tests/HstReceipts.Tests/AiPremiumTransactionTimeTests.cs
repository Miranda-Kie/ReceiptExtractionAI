using HstReceipts.Infrastructure.Extraction;
using Xunit;

namespace HstReceipts.Tests;

public sealed class AiPremiumTransactionTimeTests
{
    [Fact]
    public void ExtractAll_PrefersTransactionRecordTime_OverPosStampSeconds()
    {
        // POS stamp encodes 11:41:16; TRANSACTION RECORD prints 11:41:11.
        var text =
            """
            AI-Premium Food Mart
            Sub Total
            93.31
            HST
            0.08
            Total after Tax
            93.39
            Credit Card
            93.39
            REF #: P9260430114116
            TRANSACTION RECORD
            DATE/TIME 2026/04/30 11:41:11
            """;

        var extractor = new ReceiptFieldExtractor();
        var rows = extractor.ExtractAll(text, "AI Premium Mart.pdf");

        Assert.NotEmpty(rows);
        var row = rows[0];
        Assert.Equal("P9260430114116", row.InvoiceNumber);
        Assert.Equal("11:41:11", row.TransactionTime);
    }

    [Fact]
    public void ExtractAll_KeepsTransactionRecord_WhenSplittingMultiSlipPdf()
    {
        var text =
            """
            AI-Premium Food Mart
            Sub Total 93.31
            HST 0.08
            Total after Tax 93.39
            Credit Card 93.39
            REF #: P9260430114116
            TRANSACTION RECORD
            11:41:11
            AI-Premium Food Mart
            Sub Total 38.39
            HST 0.04
            Total after Tax 38.43
            Credit Card 38.43
            REF #: P8260409112215
            TRANSACTION RECORD
            11:22:10
            """;

        var extractor = new ReceiptFieldExtractor();
        var rows = extractor.ExtractAll(text, "AI Premium Mart.pdf");

        Assert.True(rows.Count >= 2);
        Assert.Contains(rows, r => r.InvoiceNumber == "P9260430114116" && r.TransactionTime == "11:41:11");
        Assert.Contains(rows, r => r.InvoiceNumber == "P8260409112215" && r.TransactionTime == "11:22:10");
    }

    [Fact]
    public void Finalize_PrefersTransactionRecord_OverExistingPosTime()
    {
        var row = new HstReceipts.Core.Models.ExtractedReceipt
        {
            ReceiptName = "AI Premium Mart.pdf",
            StoreName = "AI Premium Food Mart",
            InvoiceNumber = "P9260430114116",
            TransactionTime = "11:41:16",
            SourceTextPreview =
                """
                REF #: P9260430114116
                TRANSACTION RECORD
                11:41:11
                """
        };

        ReceiptFieldExtractor.FinalizeAiPremiumFoodMartRow(row);

        Assert.Equal("11:41:11", row.TransactionTime);
    }
}
