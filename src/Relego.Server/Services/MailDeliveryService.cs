using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Relego.Server.Infrastructure.Smtp;

namespace Relego.Server.Services;

/// <summary>
/// Sends mail through the effective SMTP configuration.
/// </summary>
/// <remarks>
/// There is deliberately one implementation for every environment. A separate development
/// service used to exist, reading <c>IOptions&lt;SmtpSettings&gt;</c> — the environment
/// variables — directly. That silently ignored anything saved from a client, so mail server
/// settings entered in the UI appeared to save and then had no effect on delivery.
/// </remarks>
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
        var settings = (await configuration.GetEffectiveAsync().ConfigureAwait(false)).Settings;
        message.From.Add(new MailboxAddress("Relego", settings.FromAddress));

        using var client = new SmtpClient();

        // When the user explicitly opts in to skipping certificate verification (for a
        // self-hosted relay with a self-signed cert), install a permissive callback.
        // This is a deliberate opt-in; the default is strict validation.
        if (settings.SkipCertificateVerification)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;

        try
        {
            // Auto is the only option that covers every port a self-hoster will point us at:
            // implicit TLS on 465, STARTTLS on 587 when the server advertises it, and plain
            // on 25 / 2525 for a local relay such as smtp4dev. Forcing SSL-on-connect here
            // fails against 587, which is the most common submission port there is.
            await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.Auto, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken)
                    .ConfigureAwait(false);
            }

            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Mail sent via {Host}:{Port} (secure: {Secure}).",
                settings.Host,
                settings.Port,
                client.IsSecure);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
