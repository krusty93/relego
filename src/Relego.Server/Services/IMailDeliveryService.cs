using MimeKit;

namespace Relego.Server.Services;

public interface IMailDeliveryService
{
    Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default);

    Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a pre-composed HTML recap email (MimeMessage) via SMTP.
    /// The message is produced by <see cref="HtmlEmailComposer.Compose"/>.
    /// </summary>
    Task SendHtmlRecapAsync(MimeMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a plain-text test email to the delivery channel (non-Kindle).
    /// Mirrors <see cref="SendTestEmailAsync"/> for the regular email channel.
    /// </summary>
    Task SendDeliveryTestEmailAsync(string toAddress, CancellationToken cancellationToken = default);
}
