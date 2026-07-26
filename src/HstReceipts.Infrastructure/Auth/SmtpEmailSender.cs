using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HstReceipts.Infrastructure.Auth;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpEmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpEmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(
        string toAddress,
        string subject,
        string plainBody,
        string? htmlBody = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "SMTP is not configured. Set Smtp:Enabled and Host/Username/Password/FromAddress.");
        }

        if (string.IsNullOrWhiteSpace(toAddress))
        {
            throw new ArgumentException("Recipient email is required.", nameof(toAddress));
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress.Trim(), _options.FromDisplayName),
            Subject = subject,
            Body = plainBody,
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        message.To.Add(toAddress.Trim());

        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            var plainView = AlternateView.CreateAlternateViewFromString(
                plainBody,
                Encoding.UTF8,
                MediaTypeNames.Text.Plain);
            var htmlView = AlternateView.CreateAlternateViewFromString(
                htmlBody,
                Encoding.UTF8,
                MediaTypeNames.Text.Html);
            message.AlternateViews.Add(plainView);
            message.AlternateViews.Add(htmlView);
        }

        using var client = new SmtpClient(_options.Host.Trim(), _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.Username.Trim(), _options.Password)
        };

        _logger.LogInformation("Sending email to {To} via {Host}:{Port}.", toAddress, _options.Host, _options.Port);
        await client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation("Email sent to {To}.", toAddress);
    }
}
