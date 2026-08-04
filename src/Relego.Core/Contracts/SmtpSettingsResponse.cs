namespace Relego.Core.Contracts;

/// <summary>
/// Effective outgoing mail server configuration. The password is never returned.
/// </summary>
public sealed record SmtpSettingsResponse
{
    /// <summary>SMTP host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP port.</summary>
    public int Port { get; set; }

    /// <summary>Address recaps are sent from.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>SMTP user name. Empty when the server does not require authentication.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether a password is stored. The password itself is write-only and
    /// is never sent to a client.
    /// </summary>
    public bool PasswordSet { get; set; }

    /// <summary>
    /// Where the effective values come from: <c>database</c> once saved from a client,
    /// <c>environment</c> while the values still come from configuration, or
    /// <c>default</c> when nothing has been configured.
    /// </summary>
    public string Source { get; set; } = "default";

    /// <summary>When the stored configuration was last saved. <c>null</c> when never saved.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// When <see langword="true"/>, TLS certificate validation is skipped.
    /// </summary>
    public bool SkipCertificateVerification { get; set; }
}

/// <summary>
/// Partial update for the outgoing mail server configuration.
/// Omitted properties keep their current value.
/// </summary>
public sealed record UpdateSmtpSettingsRequest
{
    /// <summary>SMTP host name.</summary>
    public string? Host { get; set; }

    /// <summary>SMTP port. Must be between 1 and 65535.</summary>
    public int? Port { get; set; }

    /// <summary>Address recaps are sent from. Must be a valid email address.</summary>
    public string? FromAddress { get; set; }

    /// <summary>
    /// SMTP user name. Pass <c>""</c> to clear it and connect without authentication.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SMTP password. Omit to keep the stored password, pass <c>""</c> to clear it.
    /// Write-only: it is never returned by <c>GET /settings/smtp</c>.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// When <see langword="true"/>, TLS certificate validation is skipped.
    /// Only enable for trusted internal mail relays with self-signed certificates.
    /// </summary>
    public bool? SkipCertificateVerification { get; set; }
}

/// <summary>
/// Result of an outgoing mail server connection test.
/// </summary>
public sealed record SmtpTestResponse
{
    /// <summary>Indicates whether the test message was delivered to the SMTP server.</summary>
    public bool Success { get; set; }

    /// <summary>Actionable description of the outcome.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Optional body for <c>POST /settings/smtp/test</c>.
/// </summary>
public sealed record SmtpTestRequest
{
    /// <summary>
    /// Address to send the test message to. Defaults to the configured delivery email,
    /// then the Kindle email, then the sender address.
    /// </summary>
    public string? ToAddress { get; set; }
}
