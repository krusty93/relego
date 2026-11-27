namespace Relego.Core.Parsing;

/// <summary>
/// A single parsed annotation: a passage, the reader's note on it, and where it sits in the source.
/// </summary>
/// <param name="Text">Passage text; empty for an annotation that is only a note.</param>
/// <param name="Location">Location as written in the source, or <c>null</c>.</param>
/// <param name="AddedOn">Original clipping timestamp, or <c>null</c>.</param>
/// <param name="Note">Reader note with its markers intact, or <c>null</c>.</param>
/// <param name="Position">Reading position reported by the source, or <c>null</c>.</param>
public record ParsedHighlight(
    string Text,
    string? Location,
    DateTimeOffset? AddedOn,
    string? Note = null,
    string? Position = null);
