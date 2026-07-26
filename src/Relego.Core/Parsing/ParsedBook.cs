namespace Relego.Core.Parsing;

/// <summary>
/// A book with its associated highlights, after deduplication and grouping.
/// A ParsedBook is never emitted with zero highlights.
/// </summary>
public record ParsedBook(
    string Title,
    string? Author,
    IReadOnlyList<ParsedHighlight> Highlights);
