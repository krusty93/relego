namespace Relego.Core.Contracts;

/// <summary>
/// Every tag in the library, used by the cross-book tag filter.
/// </summary>
public sealed record TagsResponse
{
    /// <summary>
    /// Tags ordered by annotation count descending, then case-insensitively by name; empty when the
    /// library declares none.
    /// </summary>
    public List<TagSummaryDto> Tags { get; set; } = [];
}

/// <summary>
/// One tag as the filter offers it.
/// </summary>
public sealed record TagSummaryDto
{
    /// <summary>
    /// One representative spelling of the tag — the one written on the lowest annotation id — so
    /// the reader sees their own words rather than a normalised form.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Number of annotations that carry the tag, whatever spelling they used.
    /// </summary>
    public int Count { get; set; }
}
