namespace Relego.Server.Infrastructure.Smtp;

public sealed class SmtpSettings
{
    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    public string FromAddress { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
