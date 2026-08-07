namespace Relego.Core.Contracts;

/// <summary>
/// Outcome of a file upload import (<c>POST /imports</c>).
/// </summary>
public sealed record ImportResponse
{
    /// <summary>Source that produced the file: <c>kindle</c> or <c>kobo</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Human-readable source name, e.g. <c>Kindle</c>.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Original file name as uploaded.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Number of distinct books found in the uploaded file.</summary>
    public int BooksParsed { get; set; }

    /// <summary>Number of highlights found in the uploaded file after in-file deduplication.</summary>
    public int HighlightsParsed { get; set; }

    /// <summary>Number of raw entries the parser processed.</summary>
    public int EntriesProcessed { get; set; }

    /// <summary>Number of entries dropped as duplicates within the file itself.</summary>
    public int DuplicatesInFile { get; set; }

    /// <summary>Number of highlights stored as new records.</summary>
    public int NewHighlights { get; set; }

    /// <summary>Number of highlights skipped because they already existed.</summary>
    public int DuplicateHighlights { get; set; }

    /// <summary>Number of new book records created.</summary>
    public int NewBooks { get; set; }

    /// <summary>Number of new author records created.</summary>
    public int NewAuthors { get; set; }
}
