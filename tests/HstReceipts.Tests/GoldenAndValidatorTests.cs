using System.Globalization;
using System.Text.Json;
using HstReceipts.Core.Models;
using HstReceipts.Infrastructure.Extraction;
using HstReceipts.Infrastructure.Learning;
using Xunit;

namespace HstReceipts.Tests;

public sealed class LlmFieldProposalValidatorTests
{
    [Fact]
    public void ApplyValidated_FillsInvoice_WhenTokenInOcr()
    {
        var receipt = new ExtractedReceipt
        {
            ReceiptName = "test.pdf",
            Success = true,
            SourceTextPreview = "Receipt P8260430114116 Total after tax 17.29 HST 0.04"
        };
        var proposal = new LlmFieldProposal
        {
            InvoiceNumber = "P8260430114116",
            TotalAmount = "17.29",
            GstHst = "0.04",
            Evidence = "Receipt stamp; Total after tax; HST"
        };

        var filled = LlmFieldProposalValidator.ApplyValidated(receipt, proposal, receipt.SourceTextPreview!);

        Assert.True(filled >= 3);
        Assert.Equal("P8260430114116", receipt.InvoiceNumber);
        Assert.Equal(17.29m, receipt.TotalAmount);
        Assert.Equal(0.04m, receipt.GstHst);
    }

    [Fact]
    public void ApplyValidated_RejectsInventedInvoice_NotInOcr()
    {
        var receipt = new ExtractedReceipt
        {
            ReceiptName = "test.pdf",
            Success = true,
            SourceTextPreview = "AI Premium Food Mart Total 17.29"
        };
        var proposal = new LlmFieldProposal
        {
            InvoiceNumber = "P9999999999999",
            Evidence = "hallucinated"
        };

        var filled = LlmFieldProposalValidator.ApplyValidated(receipt, proposal, receipt.SourceTextPreview!);

        Assert.Equal(0, filled);
        Assert.Null(receipt.InvoiceNumber);
    }

    [Fact]
    public void ApplyValidated_RejectsMoney_NotInOcr()
    {
        var receipt = new ExtractedReceipt { SourceTextPreview = "Total after tax 17.29" };
        var proposal = new LlmFieldProposal { TotalAmount = "99.99" };

        Assert.Equal(0, LlmFieldProposalValidator.ApplyValidated(receipt, proposal, receipt.SourceTextPreview!));
        Assert.Null(receipt.TotalAmount);
    }

    [Theory]
    [InlineData("2026-04-30", true)]
    [InlineData("1999-01-01", false)]
    [InlineData("not-a-date", false)]
    public void TryAcceptDate_YearBounds(string raw, bool expected)
    {
        var ok = LlmFieldProposalValidator.TryAcceptDate(raw, out _);
        Assert.Equal(expected, ok);
    }
}

public sealed class GoldenSetEvalTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    [Fact]
    public void GoldenSet_RuleExtractor_MeetsFieldTargets()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "golden");
        Assert.True(Directory.Exists(dir), $"Missing golden dir: {dir}");

        var cases = Directory.GetFiles(dir, "*.json")
            .Select(path => JsonSerializer.Deserialize<GoldenCase>(File.ReadAllText(path), JsonOptions)!)
            .Where(c => c is not null)
            .ToList();

        Assert.NotEmpty(cases);

        var extractor = new ReceiptFieldExtractor();
        var report = new List<string>();
        var fieldHits = 0;
        var fieldTotal = 0;

        foreach (var c in cases)
        {
            var rows = extractor.ExtractAll(c.OcrText, c.SourceFileName);
            Assert.NotEmpty(rows);
            var row = rows[0];
            ExtractedReceiptValidator.Apply(row);

            Score(c, row, ref fieldHits, ref fieldTotal, report);
        }

        var accuracy = fieldTotal == 0 ? 0 : (double)fieldHits / fieldTotal;
        report.Insert(0, $"Golden eval: {fieldHits}/{fieldTotal} fields matched ({accuracy:P0}) across {cases.Count} cases.");
        // Always emit for `dotnet test -v n` debugging.
        foreach (var line in report)
        {
            // xUnit captures this via ITestOutputHelper-less Console in some hosts; keep Assert message rich.
        }

        Assert.True(
            accuracy >= 0.70,
            string.Join(Environment.NewLine, report) + Environment.NewLine +
            $"Expected >= 70% field accuracy, got {accuracy:P1}.");
    }

    private static void Score(
        GoldenCase c,
        ExtractedReceipt row,
        ref int hits,
        ref int total,
        List<string> report)
    {
        var e = c.Expected;

        if (!string.IsNullOrWhiteSpace(e.StoreNameContains))
        {
            total++;
            if (row.StoreName?.Contains(e.StoreNameContains, StringComparison.OrdinalIgnoreCase) == true)
            {
                hits++;
            }
            else
            {
                report.Add($"FAIL [{c.Id}] storeName: got '{row.StoreName}'");
            }
        }

        if (!string.IsNullOrWhiteSpace(e.InvoiceNumber))
        {
            total++;
            if (string.Equals(row.InvoiceNumber, e.InvoiceNumber, StringComparison.OrdinalIgnoreCase))
            {
                hits++;
            }
            else
            {
                report.Add($"FAIL [{c.Id}] invoiceNumber: got '{row.InvoiceNumber}'");
            }
        }

        if (!string.IsNullOrWhiteSpace(e.Currency))
        {
            total++;
            if (string.Equals(row.Currency, e.Currency, StringComparison.OrdinalIgnoreCase))
            {
                hits++;
            }
            else
            {
                report.Add($"FAIL [{c.Id}] currency: got '{row.Currency}'");
            }
        }

        if (!string.IsNullOrWhiteSpace(e.ReceiptDate) &&
            DateOnly.TryParse(e.ReceiptDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expectedDate))
        {
            total++;
            if (row.ReceiptDate == expectedDate)
            {
                hits++;
            }
            else
            {
                report.Add($"FAIL [{c.Id}] receiptDate: got '{row.ReceiptDate}'");
            }
        }

        if (e.TotalAmount is not null)
        {
            total++;
            if (row.TotalAmount is not null && Math.Abs(row.TotalAmount.Value - e.TotalAmount.Value) <= 0.01m)
            {
                hits++;
            }
            else
            {
                report.Add($"FAIL [{c.Id}] totalAmount: got '{row.TotalAmount}'");
            }
        }

        if (e.GstHst is not null)
        {
            total++;
            if (row.GstHst is not null && Math.Abs(row.GstHst.Value - e.GstHst.Value) <= 0.01m)
            {
                hits++;
            }
            else
            {
                report.Add($"FAIL [{c.Id}] gstHst: got '{row.GstHst}'");
            }
        }
    }

    private sealed class GoldenCase
    {
        public string Id { get; set; } = string.Empty;
        public string SourceFileName { get; set; } = string.Empty;
        public string OcrText { get; set; } = string.Empty;
        public GoldenExpected Expected { get; set; } = new();
    }

    private sealed class GoldenExpected
    {
        public string? StoreNameContains { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? Currency { get; set; }
        public string? ReceiptDate { get; set; }
        public decimal? TotalAmount { get; set; }
        public decimal? GstHst { get; set; }
    }
}
