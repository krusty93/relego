namespace Relego.Server.Models;

public class User
{
    public int Id { get; set; }

    public string KindleEmail { get; set; } = string.Empty;

    /// <summary>
    /// Regular email address for HTML recap delivery.
    /// <c>null</c> = not configured. Empty string = explicitly cleared.
    /// </summary>
    public string? DeliveryEmail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
