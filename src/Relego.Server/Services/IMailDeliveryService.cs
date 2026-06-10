namespace Relego.Server.Services;

public interface IMailDeliveryService
{
    Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default);

    Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes and sends an HTML recap email via SMTP.
    /// The message is composed by <see cref="HtmlEmailComposer.Compose"/>.
    /// </summary>
    Task SendHtmlRecapAsync(IReadOnlyList<SelectionCandidate> highlights, DateTimeOffset recapDate, string toAddress, CancellationToken cancellationToken = default);
}
