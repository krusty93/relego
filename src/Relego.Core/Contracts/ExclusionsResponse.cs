namespace Relego.Core.Contracts;

/// <summary>
/// Lists the current exclusions applied to the user.
/// </summary>
public sealed record ExclusionsResponse
{
    /// <summary>
    /// Individually excluded highlights.
    /// </summary>
    public List<ExcludedHighlightDto> Highlights { get; set; } = [];

    /// <summary>
    /// Books excluded from recap selection.
    /// </summary>
    public List<ExcludedBookDto> Books { get; set; } = [];

    /// <summary>
    /// Authors excluded from recap selection.
    /// </summary>
    public List<ExcludedAuthorDto> Authors { get; set; } = [];
}

/// <summary>
/// Summary of an individually excluded highlight.
/// </summary>
public sealed record ExcludedHighlightDto
{
    /// <summary>
    /// Highlight identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Highlight text truncated to the first 100 characters.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Title of the book containing the highlight.
    /// </summary>
    public string BookTitle { get; set; } = string.Empty;
}

/// <summary>
/// Summary of an excluded book.
/// </summary>
public sealed record ExcludedBookDto
{
    /// <summary>
    /// Book identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Book title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Author display name, or <c>null</c> when the source reported none.
    /// </summary>
    public string? AuthorName { get; set; }

    /// <summary>
    /// Number of highlights stored for the book.
    /// </summary>
    public int HighlightCount { get; set; }
}

/// <summary>
/// Summary of an excluded author.
/// </summary>
public sealed record ExcludedAuthorDto
{
    /// <summary>
    /// Author identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Author display name, or <c>null</c> when the source reported none.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Number of books stored for the author.
    /// </summary>
    public int BookCount { get; set; }
}
