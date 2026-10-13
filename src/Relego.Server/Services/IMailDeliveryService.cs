using Relego.Server.Infrastructure.Smtp;

namespace Relego.Server.Services;

public interface IMailDeliveryService
{
    Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Sends a test message using optional, non-persisted SMTP settings.</summary>
    Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default, SmtpSettings? smtpSettings = null);

    /// <summary>
    /// Composes and sends an HTML recap email via SMTP.
    /// The message is composed by <see cref="HtmlEmailComposer.Compose"/>.
    /// </summary>
    Task SendHtmlRecapAsync(
        string toAddress,
        string htmlBody,
        string plainTextBody,
        string subject = "Your Relego Recap",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a multi-part notification email that is not a recap, such as the inactivity reminder.
    /// </summary>
    /// <param name="toAddress">Recipient address.</param>
    /// <param name="subject">Message subject.</param>
    /// <param name="htmlBody">HTML body.</param>
    /// <param name="plainTextBody">Plain-text alternative.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendNotificationAsync(
        string toAddress,
        string subject,
        string htmlBody,
        string plainTextBody,
        CancellationToken cancellationToken = default);
}
