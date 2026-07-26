namespace Relego.Core.Contracts;

/// <summary>
/// Paginated list of books in the library.
/// </summary>
public sealed record BooksResponse
{
    /// <summary>Total number of books matching the current filter.</summary>
    public int Total { get; set; }

    /// <summary>Current page number (1-based).</summary>
    public int Page { get; set; }

    /// <summary>Number of items per page.</summary>
    public int PageSize { get; set; }

    /// <summary>Books on the current page.</summary>
    public List<BookItemDto> Items { get; set; } = [];
}

/// <summary>
/// A single book in the library list.
/// </summary>
public sealed record BookItemDto
{
    /// <summary>Book identifier.</summary>
    public int Id { get; set; }

    /// <summary>Book title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Author identifier.</summary>
    public int AuthorId { get; set; }

    /// <summary>Author display name.</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Number of highlights stored for the book.</summary>
    public int HighlightCount { get; set; }

    /// <summary>Number of the book's highlights that are individually excluded.</summary>
    public int ExcludedHighlightCount { get; set; }

    /// <summary>Indicates whether the book itself is excluded from recaps.</summary>
    public bool Excluded { get; set; }

    /// <summary>Indicates whether the book's author is excluded from recaps.</summary>
    public bool AuthorExcluded { get; set; }
}
