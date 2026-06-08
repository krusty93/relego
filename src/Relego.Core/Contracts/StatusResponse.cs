namespace Relego.Core.Contracts;

/// <summary>
/// Aggregate status of the Relego server for the implicit MVP user.
/// </summary>
public sealed record StatusResponse
{
    /// <summary>Total number of highlights currently stored.</summary>
    public int TotalHighlights { get; set; }

    /// <summary>Total number of books currently stored.</summary>
    public int TotalBooks { get; set; }

    /// <summary>Total number of distinct authors currently stored.</summary>
    public int TotalAuthors { get; set; }

    /// <summary>Number of highlights explicitly excluded from recaps.</summary>
    public int ExcludedHighlights { get; set; }

    /// <summary>Number of books excluded from recaps.</summary>
    public int ExcludedBooks { get; set; }

    /// <summary>Number of authors excluded from recaps.</summary>
    public int ExcludedAuthors { get; set; }

    /// <summary>
    /// Next scheduled recap date-time in ISO 8601 format.
    /// <c>null</c> when no next recap has been scheduled yet.
    /// </summary>
    public string? NextRecap { get; set; }

    /// <summary>"delivered" | "failed" | null</summary>
    public string? LastRecapStatus { get; set; }

    /// <summary>Error detail when LastRecapStatus is "failed"; null otherwise.</summary>
    public string? LastRecapError { get; set; }

    /// <summary>Indicates whether a Kindle delivery email is configured for the user.</summary>
    public bool KindleEmailConfigured { get; set; }

    /// <summary>Indicates whether a regular delivery email is configured for the user.</summary>
    public bool DeliveryEmailConfigured { get; set; }
}
