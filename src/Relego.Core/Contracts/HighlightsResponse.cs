namespace Relego.Core.Contracts;

/// <summary>
/// Paginated list of highlights.
/// </summary>
public sealed record HighlightsResponse
{
    /// <summary>
    /// Total number of highlights matching the current filter.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Highlights on the current page.
    /// </summary>
    public List<HighlightItemDto> Items { get; set; } = [];
}

/// <summary>
/// A single highlight entry in a paginated list.
/// </summary>
public sealed record HighlightItemDto
{
    /// <summary>
    /// Highlight identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Book identifier.
    /// </summary>
    public int BookId { get; set; }

    /// <summary>
    /// Author identifier.
    /// </summary>
    public int AuthorId { get; set; }

    /// <summary>
    /// Full highlight text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Title of the book containing the highlight.
    /// </summary>
    public string BookTitle { get; set; } = string.Empty;

    /// <summary>
    /// Name of the author of the book, or <c>null</c> when the source reported none.
    /// </summary>
    public string? AuthorName { get; set; }

    /// <summary>
    /// Reader note attached to the passage with its markers removed, or <c>null</c> when the
    /// annotation carries no note.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Tags the note declares, in the reader's spelling; never <c>null</c>, empty when untagged.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Chapter heading level (1 to 3) the note declares, or <c>null</c> when it is not a chapter title.
    /// </summary>
    public int? HeadingLevel { get; set; }

    /// <summary>
    /// Identifier of the annotation that titles the chapter this one sits in, or <c>null</c> for the
    /// unknown chapter.
    /// </summary>
    public int? ChapterId { get; set; }

    /// <summary>
    /// Chapter titles from the root chapter down to the one this annotation sits in; empty for the
    /// unknown chapter.
    /// </summary>
    public List<string> ChapterPath { get; set; } = [];

    /// <summary>
    /// Identifier of the first annotation of this annotation's stitched group, or <c>null</c> when it
    /// is not stitched.
    /// </summary>
    public int? StitchGroupId { get; set; }

    /// <summary>
    /// Zero-based index of this annotation within its stitched group, or <c>null</c> when it is not
    /// stitched.
    /// </summary>
    public int? StitchIndex { get; set; }

    /// <summary>
    /// Passage joined across the stitched group, populated on the group's first annotation only;
    /// otherwise <c>null</c>.
    /// </summary>
    public string? StitchedText { get; set; }

    /// <summary>
    /// Reading position as reported by the source, or <c>null</c> when unknown.
    /// </summary>
    public string? Position { get; set; }

    /// <summary>
    /// Timestamp the source reported for the annotation, or <c>null</c> when unknown.
    /// </summary>
    public DateTimeOffset? AddedOn { get; set; }
}
