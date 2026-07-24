namespace HstReceipts.Core.Interfaces;

public interface ITextExtractor
{
    bool CanHandle(string fileName);
    Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}
