# Contract: `ICloudSyncProvider` (server-side provider contract)

**Location**: `src/Relego.Server/Sync/` | **Spec**: FR-016 | **Research**: R4

The **push-based counterpart** of `IHighlightSource` for cloud providers. `IHighlightSource` (file
pull, `Relego.Core/Sources`) and the local Kindle/Kobo sources are unchanged (FR-009). This contract
lives in the server because only the server consumes cloud sync; the CLI never needs it, so there is
no reason to place it in the shared library.

```csharp
public interface ICloudSyncProvider
{
    SourceDescriptor Descriptor { get; }              // Id = "kindle-cloud", DisplayName = "Kindle cloud"
    CloudSyncCapabilities Capabilities { get; }       // PushIngest, FullResync, ProgressReporting
    int InactivityReminderDays { get; }               // 45 (FR-019)
    CoverageDisclosure Disclosure { get; }            // versioned, see below
    ParseProfile Profile { get; }                     // version + region allowlist (id, name, host, sign-in patterns)
    SyncFrequencyPolicy Frequency { get; }            // AllowedMinutes = {15,30,45,60,120,240,360,720,1080,1440}, Default = 360 (validation/default only)

    // Validates and normalizes a provider payload into the existing SyncRequest.
    // Returns validation errors rather than throwing; never performs I/O.
    ProviderNormalizationResult Normalize(CloudSyncBatch batch);
}

public sealed record CoverageDisclosure(
    string Version,
    IReadOnlyList<DisclosureItem> Items);              // Kind: Coverage | Sideloaded | ExportLimit | Prerequisite | Privacy | Availability | Fees

public sealed record DisclosureItem(string Kind, string Title, string Body);
```

Rules:

- Registered once in DI: `services.AddSingleton<ICloudSyncProvider, KindleCloudProvider>()`; the
  server resolves providers via `IEnumerable<ICloudSyncProvider>` and **never** switches on `Id`
  (no enum, no per-provider branching in endpoints/repositories; endpoints use `{providerId}`).
- Transport/session mechanics (browser, cookies, notebook DOM) are **not** in the contract; the
  contract exposes only the provider-neutral batch shape and disclosure content. `Relego.Core` keeps
  only the shared `SyncRequest`/`SyncResponse` DTOs (used by server and CLI); the provider contract
  and its supporting types (`ParseProfile`, `CoverageDisclosure`, `CloudSyncBatch`,
  `SyncFrequencyPolicy`) live in `Relego.Server/Sync/`.
- `Normalize` output feeds `SyncRepository.ImportAsync` unchanged (dedup is shared).
- Adding a provider = one class + one DI line + tests (mirrors ADR-008 §5).
- `Profile.Regions` is the only source of notebook hosts: `bool TryResolveRegion(string regionId, out KindleRegion region)`
  returns false for unknown/excluded/host-shaped input. Callers never pass hosts.
- Scheduling is **not** part of the provider or the server: `Frequency` is used for validation and
  the default only. The browser extension owns routine sync timing with its `kindle-sync` alarm;
  no `ISyncScheduleService` or Quartz sync job exists (Quartz is used only by the reminder job).

Test obligations: contract test that a stub provider registered in DI is listed by
`GET /sync/providers` with no code change elsewhere; `KindleCloudProvider` normalization tests; region allowlist tests (the eight IDs `us`/`ca`/`uk`/`de`/`fr`/`it`/`es`/`br` resolve to their hosts, while `jp`/`cn`/`dk`/`ie`/`pl`/`read.amazon.com`/empty do not); frequency policy tests (exactly ten values).
