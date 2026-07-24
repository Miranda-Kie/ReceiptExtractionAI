namespace HstReceipts.Infrastructure.Extraction;

/// <summary>
/// Limits how many Tesseract OCR jobs run at once across files and PDF pages.
/// </summary>
internal static class OcrConcurrency
{
    private static readonly SemaphoreSlim Gate = new(4, 4);

    public static async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(work, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => work(cancellationToken), cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }
}
