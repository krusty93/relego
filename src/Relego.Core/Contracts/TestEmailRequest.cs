namespace Relego.Core.Contracts;

/// <summary>
/// Optional request body for POST /settings/test-email.
/// </summary>
public sealed record TestEmailRequest
{
    /// <summary>
    /// Channel to test. "kindle", "delivery", or "both".
    /// <c>null</c> = auto-detect (send to all configured channels).
    /// </summary>
    public string? Channel { get; set; }
}
