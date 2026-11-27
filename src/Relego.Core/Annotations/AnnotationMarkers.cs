namespace Relego.Core.Annotations;

/// <summary>
/// The reading vocabulary a reader wrote at the start of a note, split from the note prose.
/// </summary>
public sealed record AnnotationMarkers
{
    /// <summary>Markers for an annotation that declares none.</summary>
    public static AnnotationMarkers Empty { get; } = new();

    /// <summary>Note prose with the leading markers removed, or <c>null</c> when nothing is left.</summary>
    public string? Note { get; init; }

    /// <summary>Chapter heading level (1 to 3), or <c>null</c> when the note declares no heading.</summary>
    public int? HeadingLevel { get; init; }

    /// <summary>Stitch level (1 or greater) of a stitched passage, or <c>null</c>.</summary>
    public int? StitchLevel { get; init; }

    /// <summary>
    /// Tags written in the note, in the reader's spelling and first-seen order; never <c>null</c>.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Whether the note declared a heading, a stitch level or at least one tag.</summary>
    public bool HasMarkers => HeadingLevel is not null || StitchLevel is not null || Tags.Count > 0;
}
