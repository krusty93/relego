using Relego.Core.Contracts;
using Relego.Core.Sources;

namespace Relego.Server.Sync;

/// <summary>
/// The Kindle cloud sync provider: reads highlights pushed from the Relego browser extension, which
/// acquires them in the user's own authenticated Amazon session. The provider owns the parse
/// profile (region allowlist), the coverage disclosure and the frequency policy.
/// </summary>
public sealed class KindleCloudProvider : ICloudSyncProvider
{
    /// <summary>Stable provider id.</summary>
    public const string ProviderId = "kindle-cloud";

    /// <summary>Human-readable provider name.</summary>
    public const string DisplayName = "Kindle cloud";

    /// <summary>Version of the coverage disclosure served by this provider.</summary>
    public const string DisclosureVersion = "1";

    /// <summary>Parse-profile version mirrored by the extension.</summary>
    public const string ProfileVersion = "1";

    /// <summary>Maximum highlight text length, in characters.</summary>
    public const int MaxTextLength = 20_000;

    /// <summary>Maximum book title length, in characters.</summary>
    public const int MaxTitleLength = 1_000;

    /// <summary>Maximum author length, in characters.</summary>
    public const int MaxAuthorLength = 500;

    /// <summary>Maximum highlights accepted in a single batch.</summary>
    public const int MaxHighlightsPerBatch = 1_000;

    private static readonly IReadOnlyList<int> AllowedMinutesList =
        [15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440];

    /// <inheritdoc />
    public SourceDescriptor Descriptor { get; } = new(ProviderId, DisplayName);

    /// <inheritdoc />
    public CloudSyncCapabilities Capabilities { get; } =
        CloudSyncCapabilities.PushIngest | CloudSyncCapabilities.FullResync | CloudSyncCapabilities.ProgressReporting;

    /// <inheritdoc />
    public int InactivityReminderDays => 45;

    /// <inheritdoc />
    public CoverageDisclosure Disclosure { get; } = new(
        DisclosureVersion,
        [
            new DisclosureItem(
                "Coverage",
                "Kindle cloud highlights only",
                "Relego syncs highlights that Amazon keeps in your Kindle Notebook. Personal documents and books sideloaded to your device are not in the cloud notebook."),
            new DisclosureItem(
                "Sideloaded",
                "Books you side-loaded are not covered",
                "Highlights from side-loaded or personal documents are not in the cloud notebook. Import those from My Clippings.txt or your Kobo instead."),
            new DisclosureItem(
                "ExportLimit",
                "Some books are truncated",
                "Publishers can cap how much of a book Amazon exposes (often about 10-20%). When a book is capped Relego imports the highlights that Amazon returns and marks the book as truncated."),
            new DisclosureItem(
                "Prerequisite",
                "Your browser and this server must be running",
                "Sync runs from Chrome or Firefox with the Relego extension installed and an active Amazon sign-in, while the Relego server is running."),
            new DisclosureItem(
                "Privacy",
                "Your data goes only to your server",
                "Highlights travel from your browser to your own Relego server. Relego never sees your Amazon password and stores no Amazon session or cookies."),
            new DisclosureItem(
                "Availability",
                "Amazon can change its pages",
                "Amazon provides no API for highlights and may change its pages. When that happens Relego reports the change and asks you to update the extension."),
        ]);

    /// <inheritdoc />
    public ParseProfile Profile { get; } = new(ProfileVersion, ParseProfile.KindleRegions);

    /// <inheritdoc />
    public SyncFrequencyPolicy Frequency { get; } = new()
    {
        AllowedMinutes = AllowedMinutesList,
        Default = 360
    };

    /// <inheritdoc />
    public ProviderNormalizationResult Normalize(CloudSyncBatch batch)
    {
        if (batch is null)
            return new ProviderNormalizationResult { Errors = ["Batch must not be null."] };

        var errors = new List<string>();

        if (batch.BatchIndex < 0)
            errors.Add("batchIndex must be zero or greater.");

        var books = batch.Books;
        if (books is null)
        {
            errors.Add("books must not be null.");
            return new ProviderNormalizationResult { Errors = errors };
        }

        var totalHighlights = 0;
        var normalizedBooks = new List<CloudSyncBook>(books.Count);

        for (var bookIndex = 0; bookIndex < books.Count; bookIndex++)
        {
            var book = books[bookIndex];
            if (book is null)
            {
                errors.Add($"books[{bookIndex}] must not be null.");
                continue;
            }

            var title = book.Title?.Trim() ?? string.Empty;
            if (title.Length is < 1 or > MaxTitleLength)
                errors.Add($"books[{bookIndex}].title must be between 1 and {MaxTitleLength} characters.");

            var author = book.Author?.Trim();
            if (author is not null && author.Length > MaxAuthorLength)
                errors.Add($"books[{bookIndex}].author must be at most {MaxAuthorLength} characters.");

            var highlights = book.Highlights;
            if (highlights is null)
            {
                errors.Add($"books[{bookIndex}].highlights must not be null.");
                continue;
            }

            totalHighlights += highlights.Count;
            var normalizedHighlights = new List<CloudSyncHighlight>(highlights.Count);

            for (var highlightIndex = 0; highlightIndex < highlights.Count; highlightIndex++)
            {
                var highlight = highlights[highlightIndex];
                if (highlight is null)
                {
                    errors.Add($"books[{bookIndex}].highlights[{highlightIndex}] must not be null.");
                    continue;
                }

                var text = highlight.Text?.Trim() ?? string.Empty;
                if (text.Length is < 1 or > MaxTextLength)
                    errors.Add($"books[{bookIndex}].highlights[{highlightIndex}].text must be between 1 and {MaxTextLength} characters.");

                normalizedHighlights.Add(highlight with { Text = text });
            }

            normalizedBooks.Add(book with
            {
                Title = title,
                Author = author,
                Highlights = normalizedHighlights
            });
        }

        if (totalHighlights > MaxHighlightsPerBatch)
            errors.Add($"A batch may contain at most {MaxHighlightsPerBatch} highlights.");

        if (errors.Count > 0)
            return new ProviderNormalizationResult { Errors = errors };

        return new ProviderNormalizationResult
        {
            NormalizedBatch = batch with { Books = normalizedBooks }
        };
    }

    /// <summary>
    /// Converts a successful normalization result into the existing <see cref="SyncRequest"/> used by
    /// <c>SyncRepository.ImportAsync</c>, so dedup is shared with the local import paths.
    /// </summary>
    /// <param name="result">A valid normalization result.</param>
    /// <returns>The equivalent sync request.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="result"/> is invalid.</exception>
    public SyncRequest ToSyncRequest(ProviderNormalizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsValid || result.NormalizedBatch is null)
            throw new InvalidOperationException("Cannot convert an invalid provider normalization result.");

        var books = result.NormalizedBatch.Books;

        return new SyncRequest
        {
            Books = books.Select(book => new SyncBookRequest
            {
                Title = book.Title,
                Author = book.Author,
                Highlights = book.Highlights
                    .Select(highlight => new SyncHighlightRequest
                    {
                        Text = highlight.Text,
                        AddedOn = highlight.AddedOn
                    })
                    .ToList()
            }).ToList()
        };
    }
}
