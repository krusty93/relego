namespace Relego.Server.Models;

/// <summary>
/// Provenance for a highlight observed through a cloud provider.
/// </summary>
public sealed class HighlightProvenanceRecord
{
    /// <summary>Highlight this provenance belongs to (one row per highlight).</summary>
    public long HighlightId { get; set; }

    /// <summary>Connection that observed the highlight, or <c>null</c>.</summary>
    public long? ConnectionId { get; set; }

    /// <summary>Source id that observed the highlight, for example <c>kindle-cloud</c>.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Region the highlight was observed in, or <c>null</c>.</summary>
    public string? RegionId { get; set; }

    /// <summary>Provider-stable identifier, or <c>null</c>.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Whether export-limit truncation was observed.</summary>
    public bool Truncated { get; set; }

    /// <summary>Highlight location, or <c>null</c>.</summary>
    public string? Location { get; set; }

    /// <summary>Reader note, or <c>null</c>.</summary>
    public string? Note { get; set; }

    /// <summary>Highlight colour, or <c>null</c>.</summary>
    public string? Color { get; set; }

    /// <summary>Time the highlight was first observed.</summary>
    public string FirstSeenAt { get; set; } = string.Empty;

    /// <summary>Time the highlight was last observed, or <c>null</c>.</summary>
    public string? LastSeenAt { get; set; }
}
