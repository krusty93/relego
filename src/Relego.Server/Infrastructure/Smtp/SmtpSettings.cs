namespace Relego.Server.Infrastructure.Smtp;

public sealed class SmtpSettings
{
    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    public string FromAddress { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/>, TLS certificate validation is skipped.
    /// Useful for internal mail relays that use self-signed certificates.
    /// Never enable this against a public mail server.
    /// </summary>
    public bool SkipCertificateVerification { get; set; }
}
