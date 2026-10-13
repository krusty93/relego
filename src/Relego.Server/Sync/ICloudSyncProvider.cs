using Relego.Core.Contracts;
using Relego.Core.Sources;

namespace Relego.Server.Sync;

/// <summary>
/// The push-based counterpart of <c>IHighlightSource</c> for cloud providers. A provider describes
/// itself, exposes its coverage disclosure and region profile, and validates and normalizes a
/// provider payload into the existing <see cref="SyncRequest"/>.
/// </summary>
public interface ICloudSyncProvider
{
    /// <summary>Provider identity used for reporting and routing; never branched on.</summary>
    SourceDescriptor Descriptor { get; }

    /// <summary>Capabilities the provider supports.</summary>
    CloudSyncCapabilities Capabilities { get; }

    /// <summary>Days of inactivity after which a reminder is raised.</summary>
    int InactivityReminderDays { get; }

    /// <summary>Versioned coverage disclosure served to the user.</summary>
    CoverageDisclosure Disclosure { get; }

    /// <summary>Parse profile with the region allowlist.</summary>
    ParseProfile Profile { get; }

    /// <summary>Frequency policy used for validation and defaults only.</summary>
    SyncFrequencyPolicy Frequency { get; }

    /// <summary>
    /// Validates and normalizes a provider payload. Returns validation errors rather than throwing
    /// and never performs I/O.
    /// </summary>
    ProviderNormalizationResult Normalize(CloudSyncBatch batch);

    /// <summary>
    /// Converts a successful normalization result into the shared <see cref="SyncRequest"/> that
    /// feeds <c>SyncRepository.ImportAsync</c> unchanged, so dedup is shared.
    /// </summary>
    SyncRequest ToSyncRequest(ProviderNormalizationResult result);
}

/// <summary>Flags describing what a cloud sync provider can do.</summary>
[Flags]
public enum CloudSyncCapabilities
{
    /// <summary>No capabilities.</summary>
    None = 0,

    /// <summary>The provider accepts pushed highlight batches.</summary>
    PushIngest = 1,

    /// <summary>The provider supports an explicit full resync.</summary>
    FullResync = 2,

    /// <summary>The provider reports sync progress.</summary>
    ProgressReporting = 4,
}

/// <summary>A versioned coverage disclosure served by a provider.</summary>
/// <param name="Version">Disclosure version recorded on the connection when shown.</param>
/// <param name="Items">The disclosure items.</param>
public sealed record CoverageDisclosure(string Version, IReadOnlyList<DisclosureItem> Items);

/// <summary>A single coverage disclosure item.</summary>
/// <param name="Kind">Category, for example <c>Coverage</c> or <c>Privacy</c>.</param>
/// <param name="Title">Short title shown to the user.</param>
/// <param name="Body">Body text shown to the user.</param>
public sealed record DisclosureItem(string Kind, string Title, string Body);

/// <summary>A raw provider payload for one batch of books and highlights.</summary>
public sealed record CloudSyncBatch
{
    /// <summary>Zero-based batch index, unique per job.</summary>
    public int BatchIndex { get; init; }

    /// <summary>Books in the batch.</summary>
    public IReadOnlyList<CloudSyncBook> Books { get; init; } = [];
}

/// <summary>A book in a provider batch.</summary>
public sealed record CloudSyncBook
{
    /// <summary>Provider-stable book key, or <c>null</c>.</summary>
    public string? ExternalKey { get; init; }

    /// <summary>Book title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Book author, or <c>null</c>.</summary>
    public string? Author { get; init; }

    /// <summary>Whether the export-limit truncation warning was observed for this book.</summary>
    public bool Truncated { get; init; }

    /// <summary>Highlights in the book.</summary>
    public IReadOnlyList<CloudSyncHighlight> Highlights { get; init; } = [];
}

/// <summary>A highlight in a provider batch.</summary>
public sealed record CloudSyncHighlight
{
    /// <summary>Provider-stable highlight id, or <c>null</c>.</summary>
    public string? ExternalId { get; init; }

    /// <summary>Highlight text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Reader note, or <c>null</c>.</summary>
    public string? Note { get; init; }

    /// <summary>Location range, or <c>null</c>.</summary>
    public string? Location { get; init; }

    /// <summary>Highlight colour, or <c>null</c>.</summary>
    public string? Color { get; init; }

    /// <summary>Original clipping timestamp, or <c>null</c>.</summary>
    public DateTimeOffset? AddedOn { get; init; }
}

/// <summary>The outcome of validating and normalizing a provider payload.</summary>
public sealed record ProviderNormalizationResult
{
    /// <summary>The normalized batch when there are no errors; otherwise <c>null</c>.</summary>
    public CloudSyncBatch? NormalizedBatch { get; init; }

    /// <summary>Human-readable validation errors; empty when the batch is valid.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Whether the payload is valid.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>The frequencies a provider accepts and the default it applies.</summary>
public sealed record SyncFrequencyPolicy
{
    /// <summary>Every accepted frequency in minutes.</summary>
    public IReadOnlyList<int> AllowedMinutes { get; init; } = [];

    /// <summary>Default frequency in minutes.</summary>
    public int Default { get; init; }

    /// <summary>Whether a frequency is accepted by this policy.</summary>
    public bool IsAllowed(int minutes) => AllowedMinutes.Contains(minutes);
}
