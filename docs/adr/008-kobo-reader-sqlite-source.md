# ADR-008: Kobo Integration — `KoboReader.sqlite` Source, Import-Only Delivery

**Status:** Accepted
**Date:** 2026-06-14

## Context

Relego supports Kindle as its only highlight source: highlights are imported from the `My Clippings.txt` plain-text file written to every Kindle device (ADR-005). Feature 016 adds Kobo e-readers as a second highlight source so Kobo owners can use Relego with the same "connect via USB, run `relego sync`" workflow.

Two characteristics of Kobo devices differ fundamentally from Kindle and force explicit decisions:

1. **Highlight storage format.** Kobo does not produce a `My Clippings.txt` file. It stores all reading data — books, reading state, highlights, and notes — in a single SQLite database on the device at `<KOBO_DRIVE>/.kobo/KoboReader.sqlite`. A secondary, partial source exists as Adobe-style `.annot` XML files under `Digital Editions/Annotations/`, but it is inconsistent and incomplete.

2. **No device email delivery.** Amazon Kindle exposes a per-device "Send-to-Kindle" email address; Relego delivers EPUB recaps to it (`kindle_email`, ADR-007). **Kobo has no email-to-device equivalent and no email delivery channel at all.** The only automatable on-device delivery paths are Dropbox and Google Drive folder sync, which require per-user OAuth, are available only on newer Kobo models, and amount to a separate delivery feature.

Key tensions to resolve:

1. **Highlight source**: `KoboReader.sqlite` only, or also the partial `.annot` files?
2. **Delivery**: build a Kobo-specific delivery channel (cloud-folder EPUB sync), or reuse an existing channel?
3. **CLI UX**: how does `relego sync` distinguish a Kindle source from a Kobo source?
4. **Pipeline impact**: how much of the existing import/storage/recap pipeline must change?

## Decision

### 1. Highlight source: `KoboReader.sqlite` only

Kobo highlights are read exclusively from `<KOBO_DRIVE>/.kobo/KoboReader.sqlite`. The `.annot` Adobe-DRM annotation files are out of scope.

Highlights and notes are read from the `Bookmark` table, joined to book metadata in the `content` table:

```sql
SELECT c.Title, c.Attribution AS Author, b.Text, b.Annotation, b.Type, b.DateCreated
FROM Bookmark b
JOIN content c ON b.VolumeID = c.ContentID
WHERE b.Text IS NOT NULL AND b.Hidden = 'false'
ORDER BY c.Title, b.DateCreated;
```

- `Type = note` rows get the existing `[my note]` prefix prepended to their text (mirrors the Kindle parser).
- `dogear` / text-less rows (plain bookmarks) are skipped, exactly as Kindle bookmarks are.
- `Hidden` (soft-deleted) rows are skipped.
- The reader copies `KoboReader.sqlite` to a temporary file before opening it, because the device file may be locked or mounted read-only.

**Rationale**: `KoboReader.sqlite` is the canonical, complete record of all Kobo highlights and is the source used by every established open-source Kobo highlight tool. The `.annot` files are partial, format-inconsistent, and present only for Adobe-DRM titles, adding edge cases for negligible coverage benefit. A single authoritative source keeps the parser focused and testable.

### 2. Delivery: import-only — reuse the existing regular email channel

Feature 016 adds **no new delivery channel**. Kobo users configure `delivery_email` (the regular HTML email channel added in feature 009 / ADR-007) and read recaps in their normal inbox on phone or desktop. Cloud-folder EPUB delivery (Dropbox / Google Drive) is explicitly out of scope.

**Rationale**: Kobo has no email-to-device address, so the Kindle EPUB-by-email model cannot apply. The regular email channel already delivers fully-formed HTML recaps to any inbox and requires zero new code. Cloud-folder EPUB sync would require OAuth flows, token storage, a new composer/delivery path, and per-user folder configuration, and would only work on newer Kobo models — a substantial feature in its own right, disproportionate to the value of replicating an on-device reading experience. Scoping Feature 016 to import-only keeps it small, robust, and immediately useful.

### 3. CLI UX: auto-detect the source

`relego sync` auto-detects whether the provided path/device is a Kindle source (`My Clippings.txt`) or a Kobo source (`.kobo/KoboReader.sqlite`). The user does not pass an explicit source-type flag.

**Rationale**: Auto-detection preserves the single, uniform `relego sync` command across both device families and avoids leaking device-type knowledge into the user's muscle memory. The two sources are unambiguous to distinguish (file name, presence of a `.kobo` folder, SQLite file signature), so detection is reliable.

### 4. Pipeline: source-agnostic, no downstream changes

The Kobo reader emits the same `ParsedBook` / `ParsedHighlight` structures the Kindle parser produces. Deduplication, grouping, the sync API, server storage, the scheduler, and recap composition are unchanged and remain source-agnostic. The only net-new component is a source abstraction plus a `KoboReaderParser`.

**Rationale**: The existing pipeline already operates on a normalized highlight model. Normalizing Kobo input at the parser boundary means everything downstream is reused as-is, with no schema, API, or recap changes and no migration.

## Consequences

- **New dependency**: the CLI gains a SQLite reader (`Microsoft.Data.Sqlite`) to read `KoboReader.sqlite`.
- **New source abstraction**: a common source interface lets the Kindle and Kobo parsers feed the same pipeline; future sources follow the same pattern.
- **Zero changes** to server, storage, scheduler, recap composition, or the database schema.
- **No new delivery channel**: Kobo recaps depend on the regular email channel (feature 009) being configured. Users who want on-device reading must continue to sideload manually.
- **Locked-file handling**: the reader must copy the database before opening it, and tolerate store vs sideloaded `VolumeID` formats, soft-deleted (`Hidden`) rows, and UTF-8 content.
- **Auto-detection** must fail with an actionable error when neither a `My Clippings.txt` nor a `.kobo/KoboReader.sqlite` is found at the given path.
- **Future delivery**: Dropbox / Google Drive EPUB-to-device sync can be added later as a separate feature and ADR if a native on-device experience is desired.
- A sample `KoboReader.sqlite` fixture is added under `docs/examples/` for tests and documentation.
