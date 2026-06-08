namespace Relego.Core.Contracts;

/// <summary>
/// Effective recap settings currently configured for the implicit Relego user.
/// </summary>
public sealed record SettingsResponse
{
    /// <summary>
    /// Recap cadence currently applied.
    /// Allowed values: <c>daily</c> or <c>weekly</c>.
    /// </summary>
    public string Schedule { get; set; } = string.Empty;

    /// <summary>
    /// Day of delivery when <c>schedule</c> is <c>weekly</c>; otherwise <c>null</c>.
    /// </summary>
    public string? DeliveryDay { get; set; }

    /// <summary>
    /// Recap delivery time in <c>HH:mm</c> format.
    /// </summary>
    public string DeliveryTime { get; set; } = string.Empty;

    /// <summary>
    /// Number of highlights included in each recap.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Send-to-Kindle email address currently used for recap delivery.
    /// </summary>
    public string KindleEmail { get; set; } = string.Empty;

    /// <summary>IANA timezone identifier for the delivery schedule (e.g., "Europe/Rome").</summary>
    public string Timezone { get; set; } = string.Empty;

    /// <summary>
    /// Regular (non-Kindle) email address for HTML recap delivery.
    /// <c>null</c> when not configured.
    /// </summary>
    public string? DeliveryEmail { get; set; }
}
