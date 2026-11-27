namespace Relego.Server.Services;

public sealed record SelectionCandidate(
    int Id,
    string Text,
    string BookTitle,
    string? AuthorName,
    int Weight,
    DateTimeOffset? LastSeen,
    DateTimeOffset CreatedAt,
    int Score,
    string? Note = null);
