using MailKit.Net.Smtp;
using MimeKit;
using Relego.Server.Infrastructure.Smtp;

namespace Relego.Server.Services;

public sealed class MailDeliveryService(
    SmtpConfigurationService configuration,
    ILogger<MailDeliveryService> logger) : IMailDeliveryService
{
    public async Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default)
    {
        using var message = new MimeMessage();
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

        message.Body = new Multipart("mixed") { body, attachment };

        await SendEmailAsync(message, cancellationToken);
    }

    public async Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default)
    {
        using var message = new MimeMessage();
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = "Relego - Test Email";
        message.Body = new TextPart("plain")
        {
            Text = "This is a test email from Relego. If you received this, your SMTP configuration is working correctly."
        };

        await SendEmailAsync(message, cancellationToken);
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
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = bodyBuilder.ToMessageBody();

        await SendEmailAsync(message, cancellationToken);

        logger.LogInformation("HTML recap sent to {ToAddress}", toAddress);
    }

    // The sender address and credentials are resolved per send so a change saved from a
    // client takes effect immediately, without restarting the server.
    private async Task SendEmailAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var settings = (await configuration.GetEffectiveAsync()).Settings;
        message.From.Add(new MailboxAddress("Relego", settings.FromAddress));

        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, useSsl: true, cancellationToken);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }
}
