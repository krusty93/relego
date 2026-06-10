using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Relego.Server.Infrastructure.Smtp;

namespace Relego.Server.Services;

/// <summary>
/// Mail delivery service for the Development environment.
/// Connects to a local SMTP relay (e.g. smtp4dev) without TLS and without credentials.
/// The sender address is always "relego" regardless of configuration.
/// </summary>
public sealed class DevMailDeliveryService : IMailDeliveryService
{
    private readonly SmtpSettings _settings;

    public DevMailDeliveryService(IOptions<SmtpSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default)
    {
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Relego", _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = "Your Relego Recap";

        var body = new TextPart("plain")
        {
            Text = "Your Kindle highlight recap is attached."
        };

        var attachment = new MimePart("application", "epub+zip")
        {
            Content = new MimeContent(new MemoryStream(epubContent)),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = fileName
        };

        var multipart = new Multipart("mixed") { body, attachment };
        message.Body = multipart;

        await SendEmailAsync(message!, cancellationToken);
    }

    public async Task SendHtmlRecapAsync(
        string toAddress,
        string htmlBody,
        string plainTextBody,
        string subject = "Your Relego Recap",
        CancellationToken cancellationToken = default)
    {
        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = plainTextBody
        };
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Relego", _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = bodyBuilder.ToMessageBody();

        await SendEmailAsync(message!, cancellationToken);
    }

    public async Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default)
    {
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Relego", _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = "Relego - Test Email";
        message.Body = new TextPart("plain")
        {
            Text = "This is a test email from Relego. If you received this, your SMTP configuration is working correctly."
        };

        await SendEmailAsync(message!, cancellationToken);
    }

    private async Task SendEmailAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.None, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        finally
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }
}
