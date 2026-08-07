namespace Relego.Core.Parsing;

internal record RawClipping(
    string Title,
    string? Author,
    bool IsNote,
    string? Location,
    DateTimeOffset? AddedOn,
    string Text);
