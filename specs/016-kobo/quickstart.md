# Quick Start: Kobo Integration

**Feature**: 016-kobo
**Date**: 2026-06-24

---

## Prerequisites

- .NET 10 SDK installed (`dotnet --version` → `10.x`)
- Repository cloned and solution builds: `dotnet build src/Relego.slnx`
- Committed test fixture present: `docs/examples/kobo-highlights.sqlite`

## What this feature adds

A second highlight source — Kobo — behind the existing sync workflow. A Kobo user connects their
device via USB and runs the same sync command as a Kindle user; the system auto-detects the
device type and reads highlights from `<KOBO_DRIVE>/.kobo/KoboReader.sqlite`. Output is identical
in shape to the Kindle parser, so deduplication, grouping, sync, storage, scheduling, and recap
composition are unchanged. Delivery is import-only — recaps reach Kobo users through the existing
`delivery_email` channel.

## Project structure (new/changed)

```
src/Relego.Cli/
├── Sources/                        # NEW — source abstraction + readers + detection
│   ├── IHighlightSource.cs
│   ├── SourceDescriptor.cs         # record
│   ├── SourceProbe.cs              # record
│   ├── KindleClippingsSource.cs
│   ├── KoboReaderSource.cs
│   ├── KoboBookmarkRow.cs          # internal
│   ├── HighlightSourceResolver.cs
│   ├── ResolvedSource.cs           # record
│   └── SourceResolution.cs
├── Parsing/
│   └── HighlightAggregator.cs      # NEW — shared dedup/group (extracted from ClippingsParser)
├── Infrastructure/
│   └── KoboDetector.cs             # NEW — Kobo device probing
├── Program.cs                          # updated — register IHighlightSource sources (Kindle first) + resolver in DI
├── Import/ClippingsImportWorkflow.cs   # updated — resolve + read all detected sources via IHighlightSource
└── Commands/ImportCommand.cs           # updated — report each detected source

src/Relego.Tests/Sources/           # NEW — Kobo reader + resolver tests
docs/examples/kobo-highlights.sqlite # EXISTING fixture (do not regenerate)
```

## Add the dependency

In `src/PackageVersions.props` add the version property:

```xml
<MicrosoftDataSqliteVersion>10.0.5</MicrosoftDataSqliteVersion>
```

In `src/Relego.Cli/Relego.Cli.csproj` reference the package **and** mirror the server's audit
suppression (required — `TreatWarningsAsErrors=true`):

```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="$(MicrosoftDataSqliteVersion)" />
...
<ItemGroup>
  <!-- GHSA-2m69-gcr7-jv3q (CVE-2025-6965): mitigated as in Relego.Server.csproj. -->
  <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />
</ItemGroup>
```

> Without the `NuGetAuditSuppress`, the build fails as soon as `Microsoft.Data.Sqlite` is added.

## Build & test

```bash
# Build everything
dotnet build src/Relego.slnx

# Run the Kobo + resolver tests
dotnet test src/Relego.Tests/Relego.Tests.csproj --filter "FullyQualifiedName~Sources"
```

## Usage examples

### Reading a Kobo database directly

```csharp
using Relego.Cli.Sources;

IHighlightSource source = new KoboReaderSource();
ParseResult result = await source.ReadAsync("/Volumes/KOBOeReader/.kobo/KoboReader.sqlite", logger);
foreach (var book in result.Books)
{
    Console.WriteLine($"{book.Title} by {book.Author ?? "Unknown"}");
    foreach (var h in book.Highlights)
    {
        // Notes appear as "[my note] ..." — identical to Kindle
        Console.WriteLine($"  - {h.Text}");
    }
}
```

### Auto-detecting the source(s) (what the sync workflow does)

```csharp
// HighlightSourceResolver is injected with the registered sources (Program.cs DI, Kindle first):
//   services.AddSingleton<IHighlightSource, KindleClippingsSource>();
//   services.AddSingleton<IHighlightSource, KoboReaderSource>();
//   services.AddSingleton<HighlightSourceResolver>();
var resolver = serviceProvider.GetRequiredService<HighlightSourceResolver>();

// Pass a device root, a file path, or null to probe connected devices.
SourceResolution resolution = resolver.Resolve(userPath);

if (!resolution.Found)
{
    // Actionable error: resolution.ProbedLocations lists every Kindle and Kobo path checked.
    return;
}

// Import EVERY detected source (usually one; both when a Kindle and a Kobo are connected at once).
// A failure reading one source is reported but does not stop the others (per-source isolation).
foreach (var resolved in resolution.Sources)
{
    Console.WriteLine($"Detected {resolved.Descriptor.DisplayName} source at {resolved.ResolvedPath}");
    try
    {
        ParseResult result = await resolved.Source.ReadAsync(resolved.ResolvedPath, logger);
        // → CreateSyncRequest(result) → POST /highlights/import  (UNCHANGED)
    }
    catch (Exception ex)
    {
        // Report this source's failure and continue with the next one.
        logger?.LogError(ex, "Failed to import {Source}", resolved.Descriptor.DisplayName);
    }
}
```

## Test data (the fixture)

`docs/examples/kobo-highlights.sqlite` is a small synthetic `KoboReader.sqlite` containing
representative rows for coverage:

- multiple books with highlights,
- `note` rows (verify `[my note]` prefix),
- `dogear` / text-less rows (verify skipped),
- `Hidden = 'true'` rows (verify skipped),
- an orphaned `Bookmark` (no matching `content` — verify dropped by the join),
- UTF-8 content (CJK / diacritics).

Load it in tests by copying to a temp path (or pointing the reader at it directly — the reader
copies internally):

```csharp
var fixture = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
    "docs", "examples", "kobo-highlights.sqlite");
var result = await new KoboReaderSource().ReadAsync(fixture);

Assert.Contains(result.Books, b => b.Highlights.Any(h => h.Text.StartsWith("[my note] ")));
```

## Key design decisions

1. **Copy-then-read** — the device file is copied to a temp path and opened read-only; never
   modified (SC-007).
2. **Same output types** — Kobo emits `ParseResult` / `ParsedBook` / `ParsedHighlight`; everything
   downstream is source-agnostic (FR-010).
3. **Shared aggregation** — dedup/grouping is extracted into `HighlightAggregator`, reused by both
   readers — no behavioral drift from the Kindle path.
4. **Open source registry** — sources are self-describing (`IHighlightSource` + `SourceDescriptor`,
   no central enum) and own their own detection; the DI-injected `HighlightSourceResolver` iterates
   the registered sources with no per-source branching. A new source = implement the interface and
   register one line in `Program.cs`. Auto-detection uses file name, `.kobo` folder, and SQLite
   signature; an actionable error is raised when no source is found.
5. **Import all detected sources** — when both a Kindle and a Kobo are connected, both are imported
   in one run (order not significant) with per-source failure isolation (one source failing never
   stops the other).
6. **Import-only** — no new delivery channel; recaps use the existing `delivery_email` path.

## What this feature does NOT do

- No `.annot` Adobe-DRM file reading.
- No new on-device delivery (Dropbox / Google Drive) — future feature + ADR.
- No server, schema, sync API, scheduler, or recap-composition changes.
- No new `relego` sub-command or source-type flag.
