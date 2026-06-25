# Research: Kobo Integration

**Feature**: 016-kobo
**Phase**: 0 — Outline & Research
**Date**: 2026-06-24

All design choices below are derived from and bounded by
[ADR-008](../../docs/adr/008-kobo-reader-sqlite-source.md). They are not re-litigated here;
this document records the implementation-level decisions that follow from the ADR.

---

## 1. Highlight source: `KoboReader.sqlite` via `Bookmark` ⋈ `content`

### Decision

Read highlights and notes exclusively from `<KOBO_DRIVE>/.kobo/KoboReader.sqlite`, joining the
`Bookmark` table to the `content` table on `Bookmark.VolumeID = content.ContentID`:

```sql
SELECT c.Title, c.Attribution, b.Text, b.Annotation, b.Type, b.DateCreated, b.Hidden
FROM Bookmark b
JOIN content c ON b.VolumeID = c.ContentID
ORDER BY c.Title, b.DateCreated;
```

Filtering, note-prefixing, and skipping are applied **in C#** after the read (not purely in the
`WHERE` clause) so the reader can: distinguish dogear/text-less rows, prepend `[my note]` to
notes, and tolerate a `NULL`/unexpected `Hidden` value robustly.

### Rationale

`KoboReader.sqlite` is the canonical, complete record of Kobo highlights and is the source used
by every established open-source Kobo highlight tool (ADR-008 §1). An **INNER JOIN** is used so
orphaned `Bookmark` rows (a `VolumeID` with no matching `content` row) are dropped automatically,
satisfying "skip orphaned rows and continue" (FR-013) without extra branching. A diagnostic count
of dropped rows can be logged as a warning.

### Alternatives considered

- **`.annot` Adobe-DRM XML files** — rejected by ADR-008 (partial, format-inconsistent, DRM-only).
- **Filtering entirely in SQL** (`WHERE b.Text IS NOT NULL AND b.Hidden = 'false'`) — rejected as
  the sole mechanism: it cannot apply the `[my note]` prefix and is brittle if `Hidden` is `NULL`
  or stored differently. We keep a permissive query and normalize in code. (The ADR's illustrative
  query filters in SQL; this plan moves the same filters to code for the reasons above — behavior
  is identical.)
- **`LEFT JOIN` to log every orphan individually** — deferred; INNER JOIN + an aggregate
  warning count is simpler and meets FR-013.

---

## 2. Row classification, notes, and skipping

### Decision

For each joined row, classify by `Bookmark.Type` and content:

| Condition | Action |
|-----------|--------|
| `Hidden` is truthy (`'true'`, `1`, `true`) | **Skip** (soft-deleted) — FR-006 |
| `Type = 'dogear'` | **Skip** (plain bookmark) — FR-005 |
| `Text` and `Annotation` both null/empty | **Skip** (text-less) — FR-005 |
| `Type = 'note'` | Emit `"[my note] " + (Annotation ?? Text)` — FR-004 |
| `Type = 'highlight'` (or any other text-bearing type) | Emit `Text` as-is |

The `[my note] ` prefix string and semantics are taken **verbatim** from the Kindle parser
(`ClippingsParser.NotePrefix`) so notes are indistinguishable across sources (SC-003).

### Rationale

On Kobo, a *note* stores the user's typed comment in `Annotation` and the anchored passage in
`Text`. The recap should surface the user's words, so the emitted note text is `Annotation`
(falling back to `Text` if the annotation is empty). Highlights store the passage in `Text` and
have no annotation. Mirroring the Kindle parser's exact prefix keeps every downstream component
(grouping, dedup, recap composition) source-agnostic (FR-010, SC-005).

### Edge cases handled

- `Type = note` with empty `Annotation` → fall back to `Text`; if both empty → skip.
- `Type = highlight` with `NULL Text` → skip (text-less).
- `Hidden` stored as integer `1`/`0` or string `'true'`/`'false'` → treated truthy/falsy uniformly.

---

## 3. Copy-then-read (never modify the device)

### Decision

Before opening the database, copy `KoboReader.sqlite` to a temporary file
(`Path.Combine(Path.GetTempPath(), "relego-kobo-" + Guid + ".sqlite")`), open a **read-only**
connection on the **copy**, and delete the copy in a `finally` block. The connection string is
`Data Source=<temp>;Mode=ReadOnly;Pooling=false`.

### Rationale

The device file may be locked or the volume mounted read-only; opening it in place can fail or
risk a write (WAL/journal sidecar creation). Copying first guarantees the device file is never
touched — verifiable as byte-identical before/after a sync (SC-007, FR-007). `Mode=ReadOnly` plus
`Pooling=false` ensures no journal/WAL files are created next to the temp copy and the handle is
released promptly for deletion.

### Edge cases handled

- **Temp dir unavailable / device disconnected mid-copy** → `IOException` surfaces as an
  actionable `ParseFailed` outcome; the temp file (if any) is still cleaned up in `finally`.
- **Corrupt / non-SQLite file** → validate the first 16 bytes equal the SQLite header
  (`53 51 4C 69 74 65 20 66 6F 72 6D 61 74 20 33 00` = `"SQLite format 3\0"`) before querying;
  on mismatch, fail with an actionable "not a valid Kobo database" message rather than throwing a
  raw SQLite error.

---

## 4. Source abstraction: `IHighlightSource` (open registry)

### Decision

Introduce a single self-describing interface that both readers implement and that yields the
existing `ParseResult`. Each source owns its **identity** (a `SourceDescriptor`, not a central
enum) and its **own detection** (`Locate`):

```csharp
public sealed record SourceDescriptor(string Id, string DisplayName);

// FoundPath set when this source can read userPath (or a probed device); ProbedLocations lists
// everywhere this source looked (used to build the actionable not-found message).
public sealed record SourceProbe(string? FoundPath, IReadOnlyList<string> ProbedLocations);

public interface IHighlightSource
{
    SourceDescriptor Descriptor { get; }
    SourceProbe Locate(string? userPath);   // userPath null → probe devices; else resolve the path
    Task<ParseResult> ReadAsync(string path, ILogger? logger = null, CancellationToken cancellationToken = default);
}
```

- `KindleClippingsSource` — `Descriptor = ("kindle", "Kindle")`; `Locate` owns the `My Clippings.txt`
  filename/`documents/` directory rules and the existing `KindleDetector` device probe; `ReadAsync`
  delegates to `ClippingsParser.ParseAsync`.
- `KoboReaderSource` — `Descriptor = ("kobo", "Kobo")`; `Locate` owns the `KoboReader.sqlite` filename /
  `.kobo/` directory rules, the SQLite-header sniff, and the new `KoboDetector` device probe;
  `ReadAsync` is the SQLite reader (sections 1–3).

The deduplication + grouping tail currently inside `ClippingsParser` is extracted into a shared
internal `HighlightAggregator.Aggregate(IReadOnlyList<RawClipping>, int)` → `ParseResult`, consumed
by both readers via the shared `RawClipping` intermediate.

### Rationale

Making each source self-describing and detection-owning keeps **all** source-type knowledge at the
source boundary (ADR-008 §5), so the resolver and import workflow have zero per-source branching.
Replacing the `HighlightSourceKind` enum with a `SourceDescriptor` removes the one shared file a new
source would otherwise have to edit, and — because the descriptor is used only as a label, never
switched on — the abstraction stays open for extension (Open/Closed). Reusing `RawClipping` and
extracting the aggregator guarantees byte-identical normalization (dedup by `(Title, Author, Text)`,
group by `(Title, Author)`) across sources with zero downstream changes (FR-010, FR-011, SC-005,
SC-010). The aggregator extraction is behavior-preserving, validated by the existing Kindle parser
tests continuing to pass.

### Alternatives considered

- **A `HighlightSourceKind` enum discriminant** — rejected: a closed enum forces every new source to
  edit shared code and invites exhaustive `switch` statements (parallel edit points). A descriptor
  owned by the source avoids both.
- **A speculative external-plugin framework** (assembly scanning, third-party DLLs) — rejected as
  premature generalization (Constitution VI / YAGNI). In-process sources behind one interface,
  discovered via DI, deliver the extensibility with none of the loading/versioning complexity.
- **Duplicating dedup/grouping inside `KoboReaderSource`** — rejected; risks drift from the Kindle
  path and violates the "source-agnostic downstream" guarantee.

---

## 5. Auto-detection & multi-source import

### Decision

A `HighlightSourceResolver` is constructed from the **injected collection** of `IHighlightSource`
and resolves an input into a `SourceResolution` carrying **every** detected source (or an actionable
not-found result). It contains no per-source branching:

```csharp
public sealed record ResolvedSource(IHighlightSource Source, string ResolvedPath, SourceDescriptor Descriptor);
public sealed record SourceResolution(bool Found, IReadOnlyList<ResolvedSource> Sources, IReadOnlyList<string> ProbedLocations);

public sealed class HighlightSourceResolver(IEnumerable<IHighlightSource> sources)
{
    public SourceResolution Resolve(string? userPath)
    {
        var found = new List<ResolvedSource>();
        var probed = new List<string>();
        foreach (var s in sources)                 // order = DI registration order
        {
            var probe = s.Locate(userPath);        // each source owns its own detection
            probed.AddRange(probe.ProbedLocations);
            if (probe.FoundPath is not null)
                found.Add(new ResolvedSource(s, probe.FoundPath, s.Descriptor));
        }
        return new SourceResolution(found.Count > 0, found, probed);
    }
}
```

Each source's `Locate(userPath)` encapsulates its detection:

1. **Explicit file** — match by name (`My Clippings.txt` → Kindle; `KoboReader.sqlite` → Kobo);
   Kobo additionally sniffs the SQLite header for a SQLite file with another name.
2. **Directory** (mounted device root) — Kindle probes `<dir>/documents/My Clippings.txt` then
   `<dir>/My Clippings.txt`; Kobo probes `<dir>/.kobo/KoboReader.sqlite`.
3. **Null path** — each source probes connected devices over the same mount roots (`/Volumes`,
   `/media`, `/run/media`, drives `D:`–`G:`) via its detector (`KindleDetector` / new `KoboDetector`).

**Multiple sources detected (both devices connected):** the resolver returns **all** of them; the
import workflow imports each independently, in registration order (order is not significant), inside
a per-source `try`/`catch` — a failure reading one source is reported and the others still import
(FR-017, FR-018, spec US4). No precedence/tie-break is needed because nothing is dropped.

**No source found:** `Found == false`; `ProbedLocations` aggregates every location each source
checked, so the error names exactly what was looked for and where (FR-009, spec AS-2.3).

### Rationale

The two sources are unambiguous by file name, the presence of a `.kobo` folder, and the SQLite
signature (ADR-008 §3), so detection is reliable without a source-type flag. Pushing detection into
each source's `Locate` (rather than a central `if/else` in the resolver) is what makes the registry
open: a third source brings its own detection and is picked up by the same loop once registered
(SC-010). Importing every detected source avoids silently dropping a user's highlights when two
devices are attached, and per-source failure isolation means one bad device never costs the user the
other.

### Alternatives considered

- **A `--source kindle|kobo` flag** — rejected by ADR-008 §3 (keeps the command uniform).
- **Picking one source by precedence when both are present** — rejected: it would silently ignore a
  connected device's highlights. Importing all detected sources is safer and removes the need for an
  arbitrary tie-break rule.
- **A central `switch` in the resolver over source kind** — rejected; it is the exact per-source
  edit point the open registry eliminates.

---

## 6. Dependency: `Microsoft.Data.Sqlite` + security mitigation

### Decision

Add `Microsoft.Data.Sqlite` to `Relego.Cli`, versioned via a new `MicrosoftDataSqliteVersion`
property in `src/PackageVersions.props` (matching the server's `10.0.5`). **Critically**, mirror
the server's NuGet-audit mitigation in the CLI csproj:

```xml
<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />
```

### Rationale

The build sets `TreatWarningsAsErrors=true` (`Directory.Build.props`), and
`Microsoft.Data.Sqlite` transitively pulls `SQLitePCLRaw.lib.e_sqlite3`, which is flagged by
**GHSA-2m69-gcr7-jv3q / CVE-2025-6965** (SQLite < 3.50.2 memory corruption). The server and test
projects already suppress this advisory under the SourceGear.sqlite3 3.50.4 mitigation; **the CLI
must do the same**, or the build fails the moment the package is referenced. Centralizing the
version as an MSBuild property follows the repo's existing convention (`PollyVersion`) and lets the
server reference the same property in a follow-up. Using the same `10.0.5` the server runs avoids a
second native SQLite binary in the solution.

### Alternatives considered

- **`System.Data.SQLite` (legacy)** — rejected; older, heavier, not aligned with the server.
- **Hand-rolled SQLite file parsing** — rejected; reinvents a maintained, audited reader.
- **True Central Package Management** (`Directory.Packages.props` + `<PackageVersion>`) — out of
  scope; the repo uses MSBuild-property versioning today, and switching CPM is a separate change.

---

## 7. Performance

### Decision

Single read of the joined query into memory, then in-memory normalization and aggregation.

### Rationale

10,000 rows is trivial for SQLite + an in-memory pass (SC-008: < 5 s). The dominant cost is the
one-time file copy, which for a few-MB database is well under budget. No streaming, indexing, or
parallelism is warranted (YAGNI). `ORDER BY c.Title, b.DateCreated` gives stable, book-grouped
output that the aggregator preserves.

### Memory estimate

- 10,000 highlights × ~500 bytes ≈ ~5 MB raw + ~8–10 MB normalized — acceptable for a CLI.

---

## 8. UTF-8 / Unicode and date handling

### Decision

- Treat all text columns as UTF-8 (SQLite default); no special handling needed for CJK,
  diacritics, or RTL scripts — they flow through `string` unchanged.
- Parse `Bookmark.DateCreated` (ISO-8601, e.g. `2024-01-15T10:30:00.000`) with
  `DateTimeOffset.TryParse(..., CultureInfo.InvariantCulture, ...)`; on failure store `null`.

### Rationale

`AddedOn` is **informational only** — it is not used for deduplication or grouping (consistent
with the Kindle parser), so a failed date parse never drops a highlight (spec Assumptions). UTF-8
round-trips natively through `Microsoft.Data.Sqlite`.

---

## Summary of decisions

| # | Topic | Decision |
|---|-------|----------|
| 1 | Source | `KoboReader.sqlite`, INNER JOIN `Bookmark`⋈`content`, filter in code |
| 2 | Notes/skip | `[my note] ` + `Annotation`; skip dogear/text-less/hidden |
| 3 | Safety | Copy to temp, read-only, validate SQLite header, delete in `finally` |
| 4 | Abstraction | `IHighlightSource` (self-describing via `SourceDescriptor`, owns `Locate`) + shared `HighlightAggregator`; open registry, no central enum |
| 5 | Detection | DI-injected resolver iterates sources; each owns `Locate`; **all detected sources imported** with per-source failure isolation; actionable not-found |
| 6 | Dependency | `Microsoft.Data.Sqlite` 10.0.5 via `PackageVersions.props` + audit suppress |
| 7 | Performance | One in-memory read + aggregate; < 5 s for 10k |
| 8 | Encoding/date | UTF-8 native; `DateCreated` parsed best-effort, informational only |

**All NEEDS CLARIFICATION resolved** — ADR-008 fixes every open design question; none remain.
