namespace Relego.Core.Contracts;

/// <summary>
/// Describes a cloud sync provider advertised by the server, including the capabilities it
/// supports and the reminder threshold it declares.
/// </summary>
public sealed record SyncProviderSummary
{
    /// <summary>Stable provider id, for example <c>kindle-cloud</c>.</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>Human-readable provider name shown in the UI.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Capabilities the provider supports.</summary>
    public SyncProviderCapabilitiesDto Capabilities { get; init; } = new();

    /// <summary>Days of inactivity after which the provider raises a reminder.</summary>
    public int InactivityReminderDays { get; init; }

    /// <summary>Version of the coverage disclosure the provider currently serves.</summary>
    public string DisclosureVersion { get; init; } = string.Empty;
}

/// <summary>
/// Flags describing what a cloud sync provider is able to do.
/// </summary>
public sealed record SyncProviderCapabilitiesDto
{
    /// <summary>Whether the provider accepts pushed highlight batches.</summary>
    public bool PushIngest { get; init; }

    /// <summary>Whether the provider supports an explicit full resync.</summary>
    public bool FullResync { get; init; }

    /// <summary>Whether the provider reports sync progress.</summary>
    public bool ProgressReporting { get; init; }
}

/// <summary>
/// A validated Amazon Kindle notebook region exposed to clients by id only; the host is derived
/// server-side from the allowlist.
/// </summary>
public sealed record SyncRegionDto
{
    /// <summary>Stable region id, for example <c>uk</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable region name, for example <c>United Kingdom</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Official notebook host derived by the server, for example <c>read.amazon.co.uk</c>.</summary>
    public string Host { get; init; } = string.Empty;
}

/// <summary>
/// The sync frequency stored for a connection together with the values the extension may apply.
/// </summary>
public sealed record SyncScheduleDto
{
    /// <summary>Currently stored frequency in minutes.</summary>
    public int IntervalMinutes { get; init; }

    /// <summary>Every frequency the server accepts, in minutes, in ascending order.</summary>
    public IReadOnlyList<int> AllowedIntervalMinutes { get; init; } = [];

    /// <summary>
    /// Next due time of the extension's sync alarm as last reported in a heartbeat, or <c>null</c>
    /// when not reported or not connected. Display-only.
    /// </summary>
    public string? NextRunAt { get; init; }

    /// <summary>
    /// True when the extension has reported applying the stored <see cref="IntervalMinutes"/>.
    /// </summary>
    public bool Applied { get; init; }

    /// <summary>User-requested command awaiting pickup: <c>none</c>, <c>sync</c> or <c>full_resync</c>.</summary>
    public string PendingCommand { get; init; } = "none";
}

/// <summary>
/// The connection state of a cloud provider as seen by the management surface.
/// </summary>
public sealed record SyncConnectionDto
{
    /// <summary>Connection state: <c>not_connected</c>, <c>awaiting_pairing</c>, <c>active</c>, <c>auth_expired</c> or <c>disconnected</c>.</summary>
    public string State { get; init; } = "not_connected";

    /// <summary>Time of the first successful pairing, or <c>null</c>.</summary>
    public string? ConnectedAt { get; init; }

    /// <summary>Time of the last extension heartbeat, or <c>null</c>.</summary>
    public string? LastHeartbeatAt { get; init; }

    /// <summary>Whether the extension has reported within the heartbeat staleness window.</summary>
    public bool BrowserReporting { get; init; }

    /// <summary>Selected region, or <c>null</c> until the extension pairs.</summary>
    public SyncRegionDto? Region { get; init; }
}

/// <summary>
/// A sync job's progress and outcome as shown to the user.
/// </summary>
public sealed record SyncJobDto
{
    /// <summary>Job identifier.</summary>
    public long Id { get; init; }

    /// <summary>Job mode: <c>routine</c> or <c>full_resync</c>.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>Job status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Current phase: <c>discovering</c>, <c>reading</c>, <c>uploading</c> or <c>done</c>.</summary>
    public string? Phase { get; init; }

    /// <summary>Total books discovered, or <c>null</c> while unknown.</summary>
    public int? BooksTotal { get; init; }

    /// <summary>Books processed so far, or <c>null</c> while unknown.</summary>
    public int? BooksDone { get; init; }

    /// <summary>Highlights observed so far.</summary>
    public int HighlightsSeen { get; init; }

    /// <summary>New highlights committed so far.</summary>
    public int HighlightsNew { get; init; }

    /// <summary>Duplicate highlights skipped so far.</summary>
    public int HighlightsDuplicate { get; init; }

    /// <summary>Books where the export-limit truncation warning was observed.</summary>
    public int TruncatedBooks { get; init; }

    /// <summary>Job start time, or <c>null</c>.</summary>
    public string? StartedAt { get; init; }

    /// <summary>Job end time, or <c>null</c> while running.</summary>
    public string? EndedAt { get; init; }
}

/// <summary>
/// A sanitized failure report for a sync job, carrying no page content or secrets.
/// </summary>
public sealed record SyncFailureDto
{
    /// <summary>Stable failure code, for example <c>auth_expired</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Plain-language, sanitized failure detail, or <c>null</c>.</summary>
    public string? Detail { get; init; }

    /// <summary>Ordered recovery steps the user can follow.</summary>
    public IReadOnlyList<string> RecoverySteps { get; init; } = [];

    /// <summary>Time the failure was recorded, or <c>null</c>.</summary>
    public string? OccurredAt { get; init; }
}

/// <summary>
/// A single versioned coverage disclosure item served by a provider.
/// </summary>
public sealed record SyncDisclosureItemDto
{
    /// <summary>Category of the item, for example <c>Coverage</c> or <c>Privacy</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Short title shown to the user.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Body text shown to the user.</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// The versioned coverage disclosure served by a provider.
/// </summary>
public sealed record SyncDisclosureDto
{
    /// <summary>Disclosure version; recorded on the connection when shown.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Disclosure items, each tagged with a kind.</summary>
    public IReadOnlyList<SyncDisclosureItemDto> Items { get; init; } = [];
}

/// <summary>
/// An inactivity reminder raised for a connection.
/// </summary>
public sealed record SyncReminderDto
{
    /// <summary>Reminder identifier.</summary>
    public long Id { get; init; }

    /// <summary>Start of the quiet period this reminder covers.</summary>
    public string QuietSince { get; init; } = string.Empty;

    /// <summary>Number of quiet days that triggered the reminder.</summary>
    public int ThresholdDays { get; init; }

    /// <summary>Time the reminder was created, or <c>null</c>.</summary>
    public string? CreatedAt { get; init; }

    /// <summary>Whether the user has dismissed the reminder.</summary>
    public bool Dismissed { get; init; }
}

/// <summary>
/// The full status payload for a cloud provider: connection, schedule, jobs, failure, reminder and
/// the always-visible disclosures and prerequisites.
/// </summary>
public sealed record SyncStatusResponse
{
    /// <summary>Provider id this status belongs to.</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>Derived user-visible status.</summary>
    public string Status { get; init; } = "not_connected";

    /// <summary>Connection state and region.</summary>
    public SyncConnectionDto Connection { get; init; } = new();

    /// <summary>Stored frequency and extension-reported schedule.</summary>
    public SyncScheduleDto Schedule { get; init; } = new();

    /// <summary>The currently running job, or <c>null</c>.</summary>
    public SyncJobDto? CurrentJob { get; init; }

    /// <summary>The most recently completed job, or <c>null</c>.</summary>
    public SyncJobDto? LastCompletedJob { get; init; }

    /// <summary>The current failure report, or <c>null</c>.</summary>
    public SyncFailureDto? Failure { get; init; }

    /// <summary>The active inactivity reminder, or <c>null</c>.</summary>
    public SyncReminderDto? Reminder { get; init; }

    /// <summary>Versioned coverage disclosure, always returned.</summary>
    public SyncDisclosureDto Disclosure { get; init; } = new();

    /// <summary>Prerequisites the user must satisfy, always returned.</summary>
    public IReadOnlyList<string> Prerequisites { get; init; } = [];
}

/// <summary>
/// The one-time pairing token issued when a connection is created; the token is shown once and
/// only its hash is stored server-side.
/// </summary>
public sealed record CreateSyncConnectionResponse
{
    /// <summary>Provider id the connection belongs to.</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>The one-time pairing token. Never returned again.</summary>
    public string Token { get; init; } = string.Empty;
}

/// <summary>
/// A request to start a sync job from the extension.
/// </summary>
public sealed record SyncJobStartRequest
{
    /// <summary>Client-generated key used to make retries idempotent.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>Job mode: <c>routine</c> or <c>full_resync</c>.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>Why the job started: <c>scheduled</c>, <c>manual</c>, <c>initial</c>, <c>retry</c> or <c>region_change</c>.</summary>
    public string Trigger { get; init; } = string.Empty;
}

/// <summary>
/// Fingerprint of a known book returned when a job starts so the extension can skip unchanged
/// books on routine runs.
/// </summary>
public sealed record SyncKnownBookDto
{
    /// <summary>Provider-stable book key.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Known highlight count for the book.</summary>
    public int HighlightCount { get; init; }
}

/// <summary>
/// The response returned when a sync job starts or an existing job is reused.
/// </summary>
public sealed record SyncJobStartResponse
{
    /// <summary>Identifier of the started (or already running) job.</summary>
    public long JobId { get; init; }

    /// <summary>Known book fingerprints used to skip unchanged books on routine runs.</summary>
    public IReadOnlyList<SyncKnownBookDto> KnownBooks { get; init; } = [];
}
