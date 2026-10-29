namespace Relego.Core.Contracts;

/// <summary>Optional body for a delivery address test. Never persists settings.</summary>
public sealed record DeliveryTestRequest
{
    /// <summary>
    /// Address to test instead of the saved destination. Omit the body to test the saved address.
    /// An explicitly empty or invalid address is rejected.
    /// </summary>
    public string? ToAddress { get; set; }
}
