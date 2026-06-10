using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using Relego.Server.Infrastructure.Smtp;

namespace Relego.Server.Services;

public sealed class MailDeliveryService : IMailDeliveryService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<MailDeliveryService> _logger;

    public MailDeliveryService(IOptions<SmtpSettings> settings, ILogger<MailDeliveryService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
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

    public async Task SendHtmlRecapAsync(IReadOnlyList<SelectionCandidate> highlights, DateTimeOffset recapDate, string toAddress, CancellationToken cancellationToken = default)
    {
        using var message = HtmlEmailComposer.Compose(highlights, recapDate, toAddress, _settings.FromAddress);

        await SendEmailAsync(message, cancellationToken);

        _logger.LogInformation("HTML recap sent to {ToAddress}", toAddress);
    }

    private async Task SendEmailAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(_settings.Host, _settings.Port, useSsl: true, cancellationToken);

            if (!string.IsNullOrEmpty(_settings.Username))
            {
                await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }
}
