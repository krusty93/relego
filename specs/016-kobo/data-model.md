# Data Model: Kobo Integration

**Feature**: 016-kobo
**Phase**: 1 — Design
**Date**: 2026-06-24

This feature adds **client-side reader types** in `Relego.Cli`. It introduces **no** persistence
entities and makes **no** schema changes (FR-015). The Kobo reader normalizes device rows into the
**existing** `ParsedBook` / `ParsedHighlight` / `ParseResult` types, which downstream components
already consume unchanged.

```
KoboReader.sqlite (device)                 Normalization                Existing output (UNCHANGED)
┌─────────────────────────┐
│ Bookmark ⋈ content       │   read +      ┌──────────────┐  aggregate  ┌────────────────────────┐
│  → KoboBookmarkRow (raw) │ ───────────▶  │ RawClipping  │ ──────────▶ │ ParseResult            │
└─────────────────────────┘  classify      │ (shared)     │  dedup+group│ ├ List<ParsedBook>     │
                                            └──────────────┘             │ │  └ ParsedHighlight[]  │
                                                                         └────────────────────────┘
```

---

## New / changed types

### IHighlightSource  *(NEW — the source abstraction, `Relego.Cli/Sources/`)*

The common, **self-describing** interface through which the Kindle parser and the Kobo reader feed
the pipeline. Each source owns its identity and its own detection so the resolver needs no per-source
branching (ADR-008 §5).

| Member | Type | Notes |
|--------|------|-------|
| `Descriptor` | `SourceDescriptor` | Source identity (`Id` + `DisplayName`); used only as a label for reporting/logging, never branched on |
| `Locate(userPath)` | `SourceProbe` | Detection: resolves an explicit path or probes connected devices; reports the file it can read (if any) and every location it checked |
| `ReadAsync(path, logger?, ct)` | `Task<ParseResult>` | Reads the source at `path` and returns the normalized result |

**Invariants**:
- Implementations MUST return the same `ParseResult` shape; no source-specific output types.
- Implementations MUST NOT modify the source location.
- A new source is added by implementing this interface and registering it once in DI — no edits to
  the resolver, workflow, command, or any enum (FR-011, SC-010).

---

### SourceDescriptor / SourceProbe  *(NEW records, `Relego.Cli/Sources/`)*

```csharp
public sealed record SourceDescriptor(string Id, string DisplayName);
public sealed record SourceProbe(string? FoundPath, IReadOnlyList<string> ProbedLocations);
```

| Type | Member | Notes |
|------|--------|-------|
| `SourceDescriptor` | `Id` | Stable machine id, e.g. `"kindle"`, `"kobo"` |
| `SourceDescriptor` | `DisplayName` | Human label, e.g. `"Kindle"`, `"Kobo"` |
| `SourceProbe` | `FoundPath` | Concrete file this source can read, or `null` if not present |
| `SourceProbe` | `ProbedLocations` | Everywhere this source looked (feeds the not-found message) |

Replaces the former central `HighlightSourceKind` enum: identity lives on each source, keeping the
abstraction open for extension.

---

### KindleClippingsSource  *(NEW, `Relego.Cli/Sources/`)*

Thin `IHighlightSource` adapter over the existing static `ClippingsParser` and `KindleDetector`.

| Member | Behavior |
|--------|----------|
| `Descriptor` | `new SourceDescriptor("kindle", "Kindle")` |
| `Locate` | Owns `My Clippings.txt` / `documents/` rules + `KindleDetector` device probe; returns a `SourceProbe` |
| `ReadAsync` | `=> ClippingsParser.ParseAsync(path, logger)` |

No new parsing logic; exists so the resolver can treat every source uniformly.

---

### KoboReaderSource  *(NEW, `Relego.Cli/Sources/`)*

`IHighlightSource` that reads `KoboReader.sqlite`. Pipeline:

1. Copy the database file to a temp path.
2. Validate the SQLite header on the copy (corrupt → actionable failure).
3. Open a read-only connection on the copy; run the `Bookmark ⋈ content` query.
4. Map each row → `KoboBookmarkRow`, classify, normalize → `RawClipping`.
5. `HighlightAggregator.Aggregate(rawClippings, totalRowsConsidered)` → `ParseResult`.
6. Delete the temp copy (`finally`).

| Member | Behavior |
|--------|----------|
| `Descriptor` | `new SourceDescriptor("kobo", "Kobo")` |
| `Locate` | Owns `KoboReader.sqlite` / `.kobo/` rules + SQLite-header sniff + `KoboDetector` device probe |
| `ReadAsync` | Copy-then-read pipeline above; warnings via `ILogger` |

---

### KoboBookmarkRow  *(NEW, internal, `Relego.Cli/Sources/`)*

Intermediate projection of one joined `Bookmark ⋈ content` row, before classification. Internal to
the reader; never exposed.

| Property | Type | Source column | Notes |
|----------|------|---------------|-------|
| `Title` | `string` | `content.Title` | Book title (non-null after INNER JOIN) |
| `Author` | `string?` | `content.Attribution` | Author; nullable/empty → `null` |
| `Text` | `string?` | `Bookmark.Text` | Highlighted passage; null for pure notes/dogears |
| `Annotation` | `string?` | `Bookmark.Annotation` | User note text; null for plain highlights |
| `Type` | `string?` | `Bookmark.Type` | `highlight` \| `note` \| `dogear` \| other |
| `DateCreated` | `string?` | `Bookmark.DateCreated` | ISO-8601 string; parsed best-effort |
| `Hidden` | `string?` | `Bookmark.Hidden` | Soft-delete flag; truthy → skip |

**Classification → output** (see research §2):

| Row state | Result |
|-----------|--------|
| `Hidden` truthy | skipped (not emitted) |
| `Type = dogear` | skipped |
| `Text` and `Annotation` both empty | skipped |
| `Type = note` | `RawClipping.Text = "[my note] " + (Annotation ?? Text)`, `IsNote = true` |
| otherwise (text-bearing) | `RawClipping.Text = Text`, `IsNote = false` |

---

### RawClipping  *(EXISTING — reused as the shared raw intermediate, `Relego.Cli/Parsing/`)*

Already produced by the Kindle parser; now also produced by `KoboReaderSource`. Unchanged shape.

| Property | Type | Kindle source | Kobo source |
|----------|------|---------------|-------------|
| `Title` | `string` | title line | `content.Title` |
| `Author` | `string?` | parenthesized author | `content.Attribution` |
| `IsNote` | `bool` | metadata type == Note | `Type == note` |
| `Location` | `string?` | Kindle location string | `null` (Kobo has no location concept) |
| `AddedOn` | `DateTimeOffset?` | parsed Kindle date | parsed `DateCreated` |
| `Text` | `string` | clipping content | `Text`, or `[my note] ` + annotation |

> Note: `IsNote` carries the note flag, but the `[my note] ` prefix is applied to `Text` at the
> point of emission for Kobo (so the aggregator dedups on the final prefixed text, matching the
> Kindle path exactly).

---

### HighlightAggregator  *(NEW refactor, internal, `Relego.Cli/Parsing/`)*

Shared normalization extracted from `ClippingsParser`. Pure, no I/O.

| Member | Signature | Behavior |
|--------|-----------|----------|
| `Aggregate` | `ParseResult Aggregate(IReadOnlyList<RawClipping> clippings, int totalEntriesProcessed)` | Filter text-less, dedup by `(Title, Author, finalText)`, group by `(Title, Author)` preserving first-seen order, count duplicates; emit `ParseResult`. `totalEntriesProcessed` is the rows considered (including skipped), passed by each source since skipped rows are not in `clippings` |

**Invariants** (identical to current Kindle behavior):
- Dedup is exact, case-sensitive on `(Title, Author, Text)`; first occurrence wins.
- A `ParsedBook` is never emitted with zero highlights.
- `TotalEntriesProcessed` and `DuplicatesRemoved` are reported on the result.

---

## Unchanged output types (no modification)

### ParsedHighlight  *(EXISTING, unchanged)*

| Property | Type | Notes |
|----------|------|-------|
| `Text` | `string` | Highlight text; notes carry the `[my note] ` prefix |
| `Location` | `string?` | `null` for Kobo |
| `AddedOn` | `DateTimeOffset?` | From `DateCreated` for Kobo |

### ParsedBook  *(EXISTING, unchanged)*

| Property | Type | Notes |
|----------|------|-------|
| `Title` | `string` | Book title |
| `Author` | `string?` | Author; `null` if absent |
| `Highlights` | `IReadOnlyList<ParsedHighlight>` | ≥ 1 |

### ParseResult  *(EXISTING, unchanged)*

| Property | Type | Notes |
|----------|------|-------|
| `Books` | `IReadOnlyList<ParsedBook>` | May be empty (no importable rows → empty result, FR-012) |
| `TotalEntriesProcessed` | `int` | Rows considered (including skipped) |
| `DuplicatesRemoved` | `int` | Duplicate count |

---

## Resolution types  *(detection, `Relego.Cli/Sources/`)*

### HighlightSourceResolver  *(NEW)*

Constructed from the **injected collection** of registered `IHighlightSource`. Iterates them,
calling each source's `Locate`, and returns **every** source that resolves (research §5). No
per-source branching.

```csharp
public sealed class HighlightSourceResolver(IEnumerable<IHighlightSource> sources)
{
    public SourceResolution Resolve(string? userPath);
}
```

| Member | Signature | Notes |
|--------|-----------|-------|
| `Resolve` | `SourceResolution Resolve(string? userPath)` | File / directory / null-device detection delegated to each source; returns all detected sources (1, or several when multiple devices are connected) |

### ResolvedSource  *(NEW record)*

| Property | Type | Notes |
|----------|------|-------|
| `Source` | `IHighlightSource` | The reader to invoke |
| `ResolvedPath` | `string` | Concrete file path to read |
| `Descriptor` | `SourceDescriptor` | Source identity (for the per-source summary/report) |

### SourceResolution  *(NEW result record)*

| Property | Type | Notes |
|----------|------|-------|
| `Found` | `bool` | True when at least one source resolved |
| `Sources` | `IReadOnlyList<ResolvedSource>` | All detected sources, in DI/registration order |
| `ProbedLocations` | `IReadOnlyList<string>` | Union of every location each source searched; populated for actionable not-found errors |

**Invariants**:
- When `Found` is true, `Sources` has at least one entry; the import workflow imports each entry,
  in order, with per-source failure isolation (FR-017, FR-018).
- When `Found` is false, `Sources` is empty and `ProbedLocations` lists both the Kindle and Kobo
  paths that were checked (FR-009).

---

## Relationship to persistence models

Unchanged from the Kindle path. `ParseResult` → `SyncRequest` mapping
(`ClippingsImportWorkflow.CreateSyncRequest`) and all server-side storage are reused verbatim
(FR-010, FR-015). Kobo introduces **no** new persistence entity.

---

## File layout

```
src/Relego.Cli/
├── Parsing/
│   ├── RawClipping.cs            # reused (unchanged shape)
│   ├── HighlightAggregator.cs    # NEW (extracted dedup/group)
│   ├── ClippingsParser.cs        # delegates to HighlightAggregator
│   ├── ParsedHighlight.cs        # unchanged
│   ├── ParsedBook.cs             # unchanged
│   └── ParseResult.cs            # unchanged
├── Sources/
│   ├── IHighlightSource.cs       # NEW
│   ├── SourceDescriptor.cs       # NEW (record; replaces the enum)
│   ├── SourceProbe.cs            # NEW (record)
│   ├── KindleClippingsSource.cs  # NEW
│   ├── KoboReaderSource.cs       # NEW
│   ├── KoboBookmarkRow.cs        # NEW (internal)
│   ├── HighlightSourceResolver.cs# NEW
│   ├── ResolvedSource.cs         # NEW (record)
│   └── SourceResolution.cs       # NEW
└── Infrastructure/
    └── KoboDetector.cs           # NEW (device probing)
```
