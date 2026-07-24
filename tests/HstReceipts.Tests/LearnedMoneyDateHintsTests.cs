using HstReceipts.Core.Models;
using HstReceipts.Infrastructure.Learning;
using Xunit;

namespace HstReceipts.Tests;

public sealed class LearnedMoneyDateHintsTests
{
    [Fact]
    public void BuildMoneyHint_FindsHstLabel()
    {
        var receipt = new ExtractedReceipt
        {
            SourceTextPreview = "Sub Total 17.25\nHST 0.04\nTotal after tax 17.29"
        };

        var hint = LearnedMoneyDateHints.BuildMoneyHint(receipt, 0.04m);

        Assert.Equal("label:HST", hint);
    }

    [Fact]
    public void TryApplyMoneyHint_ExtractsTotalAfterTax()
    {
        var receipt = new ExtractedReceipt
        {
            SourceTextPreview = "Sub Total\n10.00\nHST\n1.30\nTotal after tax\n11.30"
        };

        var ok = LearnedMoneyDateHints.TryApplyMoneyHint(
            "label:Total after tax",
            receipt,
            v => receipt.TotalAmount = v,
            () => receipt.TotalAmount,
            "total",
            forceReplace: true);

        Assert.True(ok);
        Assert.Equal(11.30m, receipt.TotalAmount);
    }

    [Fact]
    public void AmountsFailAuthentication_DetectsMismatch()
    {
        var receipt = new ExtractedReceipt
        {
            Subtotal = 10m,
            GstHst = 1m,
            TotalAmount = 20m
        };

        Assert.True(LearnedMoneyDateHints.AmountsFailAuthentication(receipt));
    }

    [Fact]
    public void NeedsEnrichment_TrueWhenAmountsBroken()
    {
        var receipt = new ExtractedReceipt
        {
            StoreName = "Test",
            InvoiceNumber = "12345",
            Currency = "CAD",
            ReceiptDate = new DateOnly(2026, 4, 1),
            Subtotal = 10m,
            GstHst = 1m,
            TotalAmount = 99m,
            SourceTextPreview = "ocr"
        };

        Assert.True(LlmFieldProposalValidator.NeedsEnrichment(receipt));
    }
}
