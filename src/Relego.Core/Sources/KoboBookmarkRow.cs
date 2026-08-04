namespace Relego.Core.Sources;

/// <summary>
/// Intermediate projection of one joined <c>Bookmark ⋈ content</c> row, before
/// classification into a <see cref="Parsing.RawClipping"/>. Internal to the Kobo
/// reader; never exposed.
/// </summary>
/// <param name="Title">Book title (<c>content.Title</c>; non-null after the INNER JOIN).</param>
/// <param name="Author">Author (<c>content.Attribution</c>); nullable/empty → <c>null</c>.</param>
/// <param name="Text">Highlighted passage (<c>Bookmark.Text</c>); null for pure notes/dogears.</param>
/// <param name="Annotation">User note text (<c>Bookmark.Annotation</c>); null for plain highlights.</param>
/// <param name="Type"><c>Bookmark.Type</c>: <c>highlight</c> | <c>note</c> | <c>dogear</c> | other.</param>
/// <param name="DateCreated">ISO-8601 string (<c>Bookmark.DateCreated</c>); parsed best-effort.</param>
/// <param name="Hidden">Soft-delete flag (<c>Bookmark.Hidden</c>); truthy → skip.</param>
internal sealed record KoboBookmarkRow(
    string Title,
    string? Author,
    string? Text,
    string? Annotation,
    string? Type,
    string? DateCreated,
    string? Hidden);
