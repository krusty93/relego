# Implementation Plan: Kobo Integration

**Branch**: `task/016-kobo-design` | **Date**: 2026-06-24 | **Spec**: [spec.md](spec.md)
**ADR**: [ADR-008 — Kobo Integration: `KoboReader.sqlite` Source, Import-Only Delivery](../../docs/adr/008-kobo-reader-sqlite-source.md)
**Input**: Feature specification from `/specs/016-kobo/spec.md`

## Summary

Add Kobo e-readers as a second highlight source behind the existing sync workflow. A new
`KoboReaderSource` reads highlights and notes from `<KOBO_DRIVE>/.kobo/KoboReader.sqlite`
(copied to a temp file first), normalizes them into the **same** `ParsedBook` / `ParsedHighlight`
structures the Kindle parser already produces, and feeds them through the unchanged
deduplication → grouping → sync → storage → scheduler → recap pipeline. An **open source
registry** — a self-describing `IHighlightSource` (identity via a `SourceDescriptor`, not a central
enum; each source owns its own detection) plus a DI-injected `HighlightSourceResolver` — lets the
existing Kindle parser and the new Kobo reader plug in, auto-detecting Kindle (`My Clippings.txt`)
vs Kobo (`.kobo/KoboReader.sqlite`) from the supplied path or connected device with no source-type
flag. When **both** devices are connected, **both** are imported in one run with per-source failure
isolation. Adding a future source is "implement `IHighlightSource` and register one line in DI". The
feature is **import-only**: Kobo recaps reach users through the existing regular email channel
(`delivery_email`, feature 009 / ADR-007). Zero server, schema, API, scheduler, or
recap-composition changes.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0` TFM)
**Primary Dependencies**: `Microsoft.Data.Sqlite` (new — read `KoboReader.sqlite`); `Microsoft.Extensions.Logging.Abstractions` (`ILogger` warnings for skipped rows); `Spectre.Console` (existing — sync summary UX)
**Storage**: Reads a **client-side, read-only copy** of the device's `KoboReader.sqlite`. No application database, schema, or persistence is touched (server SQLite is untouched).
**Testing**: xUnit (via `Relego.Tests`), using the committed `docs/examples/kobo-highlights.sqlite` fixture
**Target Platform**: Cross-platform CLI (.NET 10 runtime) — macOS, Linux, Windows USB-mounted devices
**Project Type**: CLI-exclusive logic in `Relego.Cli` (no shared/server code)
**Performance Goals**: 10,000 Kobo highlights read, normalized, deduplicated, and grouped in < 5 seconds (SC-008)
**Constraints**: Never modify the on-device file (copy-then-read); local processing only; no new delivery channel; no new `relego` sub-command surface beyond auto-detection
**Scale/Scope**: Typical library: hundreds–thousands of highlights; stress: 10,000+. One net-new reader plus an open source registry; everything downstream reused as-is.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Client/Server Separation | **PASS** | All new code lives in `Relego.Cli`; the server, schema, and API are untouched (FR-015). |
| II. CLI-First, No GUI | **PASS** | No GUI. Auto-detection keeps the single `relego` sync command; errors are actionable (FR-009). |
| III. Zero-Config Onboarding | **PASS** | No new configuration. A Kobo user runs the same sync command as a Kindle user (FR-001, SC-001). |
| IV. Local Processing Only | **PASS** | Reading and normalization happen entirely on-device-host; no third-party calls (FR-007, FR-010). |
| V. Tests Ship with the Code | **PASS** | Unit tests for the Kobo reader, the resolver, and notes handling ship with the implementation using the committed fixture (FR-016). |
| VI. Simplicity / YAGNI | **PASS** | One reader behind a thin, self-describing source interface; the resolver is a single loop over DI-registered sources (no per-source branching, no central enum). `.annot` files, cloud-folder delivery, a source-type flag, and dynamic/external plugin loading are explicitly out of scope. |
| Tech: C# / .NET 10 only | **PASS** | All code is C#. |
| Tech: Storage = SQLite | **PASS** | Uses `Microsoft.Data.Sqlite` (already used by the server) to read a copy of the device DB; adds no second app database. |
| Tech: No raw `Console.WriteLine` | **PASS** | Reader returns data + `ILogger` warnings; user-facing output stays in the existing Spectre.Console summary. |
| Tech: CLI UX = Spectre.Console | **PASS** | Reuses the existing import summary panel; the chosen source is reported through it. |
| Exclusion: No AI summarization / scraping / 3rd-party SaaS | **PASS** | Not applicable; Kobo data is read locally from the device. |

**Post-design re-check**: All gates still pass. The only new dependency is `Microsoft.Data.Sqlite` (already a server dependency, so no new runtime/language is introduced). The open source registry is a single small interface (descriptor + `Locate` + `ReadAsync`) plus a DI-injected resolver — no speculative plugin framework, no assembly scanning. No constitution violations; Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/016-kobo/
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0: Kobo schema, copy-then-read, detection, dependency decisions
├── data-model.md        # Phase 1: Kobo row entities, source abstraction, mapping to ParsedBook
├── quickstart.md        # Phase 1: developer quick-start for the Kobo reader
├── contracts/
│   └── kobo-sqlite-read.md   # Phase 1: KoboReader.sqlite read contract + IHighlightSource contract
└── tasks.md             # Phase 2 (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Relego.Cli/
├── Parsing/                        # EXISTING — Kindle parser + shared normalization
│   ├── RawClipping.cs              #   (reused as the shared raw intermediate)
│   ├── ParsedHighlight.cs          #   UNCHANGED public output type
│   ├── ParsedBook.cs               #   UNCHANGED public output type
│   ├── ParseResult.cs              #   UNCHANGED public output type
│   ├── ClippingsParser.cs          #   refactored: delegates dedup/group to HighlightAggregator
│   └── HighlightAggregator.cs      #   NEW (refactor): shared dedup + grouping → ParseResult
├── Sources/                        # NEW — open source registry: readers + descriptors + detection
│   ├── IHighlightSource.cs         #   NEW: self-describing source (Descriptor + Locate + ReadAsync)
│   ├── SourceDescriptor.cs         #   NEW: source identity record (replaces the enum)
│   ├── SourceProbe.cs              #   NEW: Locate result (FoundPath + ProbedLocations)
│   ├── KindleClippingsSource.cs    #   NEW: IHighlightSource over the existing ClippingsParser
│   ├── KoboReaderSource.cs         #   NEW: IHighlightSource reading KoboReader.sqlite
│   ├── ResolvedSource.cs           #   NEW: a detected source + resolved path
│   ├── SourceResolution.cs         #   NEW: all detected sources (1, or several when both connected)
│   └── HighlightSourceResolver.cs  #   NEW: iterates DI-injected sources; returns all detected
├── Infrastructure/
│   ├── KindleDetector.cs           #   EXISTING — Kindle device probing
│   └── KoboDetector.cs             #   NEW: Kobo device probing (.kobo/KoboReader.sqlite)
├── Program.cs                      #   updated: register sources (Kindle first) + resolver in DI
├── Import/
│   └── ClippingsImportWorkflow.cs  #   updated: resolve via HighlightSourceResolver, import ALL
│                                   #   detected sources (per-source failure isolation);
│                                   #   CreateSyncRequest UNCHANGED
└── Commands/
    └── ImportCommand.cs            #   updated: report each detected source; no new sub-command

src/Relego.Server/                  # UNCHANGED (FR-015)
src/Relego.Core/                    # UNCHANGED

src/Relego.Tests/
├── Parsing/                        # EXISTING Kindle parser tests (unchanged behavior)
└── Sources/                        # NEW — Kobo reader, resolver, and notes tests
    ├── KoboReaderSourceTests.cs
    └── HighlightSourceResolverTests.cs

docs/examples/
└── kobo-highlights.sqlite          # EXISTING committed fixture (do not regenerate)

src/PackageVersions.props           # add MicrosoftDataSqliteVersion property
```

**Structure Decision**: All net-new code is added to the existing `Relego.Cli` project — no new
project (YAGNI, mirrors feature 003). A new `Sources/` namespace holds the open source registry:
self-describing readers (each carrying its `SourceDescriptor` and owning its `Locate` detection)
and a DI-injected `HighlightSourceResolver` that iterates the registered sources with no per-source
branching. The Kindle parser's deduplication/grouping tail is extracted into a shared
`HighlightAggregator` so both readers emit identical `ParseResult` values without duplicating
logic. Sources are registered (Kindle first) in `Program.cs` DI; the import workflow resolves and
imports **all** detected sources with per-source failure isolation; everything from
`CreateSyncRequest` onward is byte-for-byte unchanged. Adding a future source requires only
implementing `IHighlightSource` and registering one line — no resolver, workflow, command, or enum
edits (Open/Closed; ADR-008 §5).

> **Naming note**: The sync workflow is implemented today by `ImportCommand` (registered as the
> `import` sub-command) and `ClippingsImportWorkflow`. The spec and ADR refer to this user action
> as `relego sync`. This plan uses the existing code names; the user-facing command surface is
> **not** changed by this feature (only the detection logic behind it becomes source-aware).

## Complexity Tracking

No constitution violations. No complexity justification required.
