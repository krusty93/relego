namespace Relego.Core.Contracts;

/// <summary>
/// The chapter navigation of one book: its chapters in reading order plus the unknown chapter,
/// which always exists so annotations outside any chapter stay reachable.
/// </summary>
public sealed record TableOfContentsResponse
{
    /// <summary>Book the navigation belongs to.</summary>
    public int BookId { get; set; }

    /// <summary>
    /// <c>true</c> when the book declares at least one level-1 heading. When <c>false</c> the
    /// reader is offered no navigation at all.
    /// </summary>
    public bool HasTableOfContents { get; set; }

    /// <summary>
    /// Chapters in reading order, with nesting expressed by <see cref="ChapterDto.ParentId"/>.
    /// Empty when <see cref="HasTableOfContents"/> is <c>false</c>.
    /// </summary>
    public List<ChapterDto> Chapters { get; set; } = [];

    /// <summary>
    /// The unknown chapter: annotations that precede the first heading, or all annotations of a
    /// book without headings. Its <see cref="ChapterDto.Id"/> is always <c>null</c>.
    /// </summary>
    public ChapterDto UnknownChapter { get; set; } = new();
}

/// <summary>
/// A chapter in a book's table of contents. Identity is the titling annotation's id, so two
/// chapters with the same title remain distinct.
/// </summary>
public sealed record ChapterDto
{
    /// <summary>Id of the annotation that titles the chapter, or <c>null</c> for the unknown chapter.</summary>
    public int? Id { get; set; }

    /// <summary>Chapter title, or <c>Unknown chapter</c> for the unknown chapter.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Heading level, 1 to 3, or <c>0</c> for the unknown chapter.</summary>
    public int Level { get; set; }

    /// <summary>Nearest preceding chapter with a strictly smaller level, or <c>null</c> at the top level.</summary>
    public int? ParentId { get; set; }

    /// <summary>Section summary the titling annotation carries, or <c>null</c>.</summary>
    public string? Note { get; set; }

    /// <summary>Source position the titling annotation carries, or <c>null</c>.</summary>
    public string? Position { get; set; }

    /// <summary>Source timestamp the titling annotation carries, or <c>null</c>.</summary>
    public DateTimeOffset? AddedOn { get; set; }

    /// <summary>Number of annotations in this chapter, including every descendant chapter's.</summary>
    public int AnnotationCount { get; set; }
}
