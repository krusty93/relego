# Contract: KoboReader.sqlite Read & `IHighlightSource`

**Feature**: 016-kobo
**Phase**: 1 — Design
**Date**: 2026-06-24

This feature changes **no** REST/sync API contract — the existing `POST /highlights/import`
payload (`SyncRequest`) is reused unchanged (FR-010, FR-015). The contracts documented here are
**CLI-side boundaries**:

1. The **read contract** against the third-party `KoboReader.sqlite` schema (an external interface
   Relego depends on).
2. The internal **`IHighlightSource`** abstraction contract.

---

## 1. KoboReader.sqlite read contract

### 1.1 Source location

```
<KOBO_DRIVE>/.kobo/KoboReader.sqlite
```

The reader operates on a **temporary copy** of this file (read-only). The on-device file is never
opened for writing and is byte-identical before/after a sync (SC-007).

### 1.2 Tables consumed (read-only)

Only the following columns are read. The reader does not depend on any other table or column and
issues no writes, `PRAGMA`s with side effects, or migrations.

**`Bookmark`**

| Column | Type (SQLite) | Used for |
|--------|---------------|----------|
| `VolumeID` | TEXT | Join key → `content.ContentID` |
| `Text` | TEXT (nullable) | Highlighted passage |
| `Annotation` | TEXT (nullable) | User note text |
| `Type` | TEXT | `highlight` \| `note` \| `dogear` \| other |
| `DateCreated` | TEXT (ISO-8601, nullable) | `AddedOn` (informational) |
| `Hidden` | TEXT/INTEGER (nullable) | Soft-delete flag |

**`content`**

| Column | Type (SQLite) | Used for |
|--------|---------------|----------|
| `ContentID` | TEXT | Join key ← `Bookmark.VolumeID` |
| `Title` | TEXT | Book title |
| `Attribution` | TEXT (nullable) | Author |

### 1.3 Query

```sql
SELECT c.Title, c.Attribution, b.Text, b.Annotation, b.Type, b.DateCreated, b.Hidden
FROM Bookmark b
JOIN content c ON b.VolumeID = c.ContentID
ORDER BY c.Title, b.DateCreated;
```

- **INNER JOIN** drops orphaned `Bookmark` rows (no matching `content`) — satisfies "skip orphaned
  rows and continue" (FR-013).
- Row-level filtering (hidden / dogear / text-less) and note-prefixing are applied **in code**
  after the read (see §1.4).

### 1.4 Row processing rules

| Input condition | Output |
|-----------------|--------|
| `Hidden` truthy (`'true'`, `true`, `1`) | **skip** (FR-006) |
| `Type = 'dogear'` | **skip** (FR-005) |
| `Text` and `Annotation` both null/empty | **skip** (FR-005) |
| `Type = 'note'` | emit `"[my note] " + (Annotation ?? Text)` (FR-004) |
| text-bearing highlight | emit `Text` verbatim |

Emitted rows become `RawClipping` and flow into `HighlightAggregator` for the **same**
deduplication and grouping the Kindle parser uses (FR-010).

### 1.5 Pre-read validation

| Check | On failure |
|-------|------------|
| File copy succeeds | Actionable `ParseFailed` (temp dir / device error) |
| First 16 bytes == `"SQLite format 3\0"` | Actionable "not a valid Kobo database" error |
| `Bookmark` and `content` tables exist | Actionable error naming the missing table |

### 1.6 Guarantees

- **Read-only**: no `INSERT`/`UPDATE`/`DELETE`/`PRAGMA` with side effects; connection opened
  `Mode=ReadOnly`, `Pooling=false`, on the temp copy only.
- **Cleanup**: the temp copy is deleted in a `finally` block regardless of outcome.
- **Empty source**: a database with no importable rows yields an empty `ParseResult`, not an
  error (FR-012).
- **Encoding**: UTF-8 text round-trips unchanged (CJK / diacritics / RTL).

---

## 2. `IHighlightSource` contract

```csharp
namespace Relego.Cli.Sources;

public sealed record SourceDescriptor(string Id, string DisplayName);
public sealed record SourceProbe(string? FoundPath, IReadOnlyList<string> ProbedLocations);

public interface IHighlightSource
{
    // Source identity — used only as a label for reporting/logging, never branched on.
    SourceDescriptor Descriptor { get; }

    // Detection owned by the source: userPath null → probe connected devices; else resolve the path.
    // FoundPath is the concrete file to read (or null); ProbedLocations lists everywhere it looked.
    SourceProbe Locate(string? userPath);

    Task<ParseResult> ReadAsync(
        string path,
        ILogger? logger = null,
        CancellationToken cancellationToken = default);
}
```

### Behavioral contract

| Guarantee | Detail |
|-----------|--------|
| Self-describing | Carries its own `Descriptor`; no central enum. A new source is added by implementing this interface and registering it once in DI — no resolver/workflow/command/enum edits. |
| Owns detection | `Locate` encapsulates this source's filename/directory/device rules; the resolver does not branch per source. |
| Uniform output | Returns the existing `ParseResult`; no source-specific output types. |
| Non-destructive | MUST NOT modify the source path/device. |
| Skip-and-warn | Malformed/orphaned rows are skipped and logged via `logger`; never throws on bad rows. |
| Empty-safe | A source with no importable highlights returns an empty `ParseResult`. |
| Cancellable | Honors `cancellationToken`. |

### Implementations

| Type | `Descriptor` | Reads |
|------|--------------|-------|
| `KindleClippingsSource` | `("kindle", "Kindle")` | `My Clippings.txt` (delegates to `ClippingsParser`) |
| `KoboReaderSource` | `("kobo", "Kobo")` | `KoboReader.sqlite` (this contract, §1) |

---

## 3. Auto-detection contract (`HighlightSourceResolver`)

Constructed from the injected `IEnumerable<IHighlightSource>`. Calls each source's `Locate` and
returns a `SourceResolution` with **every** detected source (no per-source branching, no precedence).

| Input | Resolved sources |
|-------|------------------|
| File named `My Clippings.txt` | Kindle |
| File named `KoboReader.sqlite` | Kobo |
| File with SQLite header, other name | Kobo |
| Directory containing `.kobo/KoboReader.sqlite` | Kobo |
| Directory containing `documents/My Clippings.txt` or `My Clippings.txt` | Kindle |
| Directory/device containing **both** | **Both** — each imported in one run, order not significant (FR-017, FR-018) |
| No path → device probe finds exactly one | that source |
| No path → device probe finds several | all found sources |
| Neither found | `Found = false`; error lists every probed location (FR-009) |

When multiple sources resolve, the import workflow imports each independently inside a per-source
`try`/`catch`: a failure reading one source is reported and the others still import (spec US4).
Each resolved source's `DisplayName` is surfaced in the sync summary.

---

## 4. Unchanged contract (explicitly out of scope)

- `POST /highlights/import` request/response (`SyncRequest` / `SyncResponse`) — **unchanged**.
- Database schema, scheduler, recap composition, and email delivery — **unchanged** (FR-015).
- Kobo recaps are delivered via the existing `delivery_email` channel (feature 009 / ADR-007);
  no new delivery contract is introduced (FR-014).
