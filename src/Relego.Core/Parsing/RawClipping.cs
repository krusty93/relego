namespace Relego.Core.Parsing;

/// <summary>A single entry a source read, before notes are attached and duplicates removed.</summary>
/// <param name="Title">Book title.</param>
/// <param name="Author">Book author, or <c>null</c>.</param>
/// <param name="Location">Source position as written by the reader, or <c>null</c>.</param>
/// <param name="AddedOn">When the entry was created, or <c>null</c> when the source omits it.</param>
/// <param name="Text">The highlighted passage, or <see cref="string.Empty"/> for a note or bookmark.</param>
/// <param name="Note">The reader's annotation, or <c>null</c> when the entry carries none.</param>
internal record RawClipping(
    string Title,
    string? Author,
    string? Location,
    DateTimeOffset? AddedOn,
    string Text,
    string? Note = null);
