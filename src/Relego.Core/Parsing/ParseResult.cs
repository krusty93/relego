namespace Relego.Core.Parsing;

/// <summary>
/// The complete output of parsing a Kindle clippings file.
/// </summary>
public record ParseResult(
    IReadOnlyList<ParsedBook> Books,
    int TotalEntriesProcessed,
    int DuplicatesRemoved);
