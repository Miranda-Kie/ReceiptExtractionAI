namespace HstReceipts.Core.Interfaces;

public interface IEmailSender
{
    bool IsConfigured { get; }

    Task SendAsync(
        string toAddress,
        string subject,
        string plainBody,
        string? htmlBody = null,
        CancellationToken cancellationToken = default);
}
