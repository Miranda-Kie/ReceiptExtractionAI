namespace HstReceipts.Core.Options;

public class SmtpEmailOptions
{
    public const string SectionName = "Smtp";

    public bool Enabled { get; set; }

    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    /// <summary>SMTP login (for Gmail, the full address).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>SMTP password or Gmail App Password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>From address shown to recipients.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromDisplayName { get; set; } = "HST Receipts";

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(Host) &&
        Port > 0 &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(FromAddress);
}
