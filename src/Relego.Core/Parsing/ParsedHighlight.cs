namespace Relego.Core.Parsing;

/// <summary>
/// A single parsed highlight or note from a Kindle clippings file.
/// Notes have their text prefixed with "[my note] ".
/// </summary>
public record ParsedHighlight(
    string Text,
    string? Location,
    DateTimeOffset? AddedOn);
