# ADR-008: Kobo Integration — `KoboReader.sqlite` Source, Import-Only Delivery

**Status:** Accepted
**Date:** 2026-06-14
**Updated:** 2026-06-25 — added the open source-registry extensibility model (§5) and multi-source import behaviour (§3).

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

### 3. CLI UX: auto-detect and import all connected sources

`relego sync` auto-detects the source(s) from the provided path/device without an explicit source-type flag. When a single source is found it is imported. When **both** a Kindle (`My Clippings.txt`) and a Kobo (`.kobo/KoboReader.sqlite`) source are present at the same time, **both are imported** in one run. Import order is not significant; if reading one source fails, the import continues with the other(s) and the failure is reported to the user (per-source failure isolation). When no source is found, the command fails with an actionable error naming every probed location.

**Rationale**: Auto-detection preserves the single, uniform `relego sync` command across both device families and avoids leaking device-type knowledge into the user's muscle memory. The two sources are unambiguous to distinguish (file name, presence of a `.kobo` folder, SQLite file signature), so detection is reliable. Importing all connected sources — rather than picking one by precedence — avoids silently dropping a user's highlights when two devices happen to be attached (an uncommon but valid setup); per-source failure isolation means a problem reading one device never costs the user the other.

### 4. Pipeline: source-agnostic, no downstream changes

The Kobo reader emits the same `ParsedBook` / `ParsedHighlight` structures the Kindle parser produces. Deduplication, grouping, the sync API, server storage, the scheduler, and recap composition are unchanged and remain source-agnostic. The only net-new component is a source abstraction plus a `KoboReaderParser`.

**Rationale**: The existing pipeline already operates on a normalized highlight model. Normalizing Kobo input at the parser boundary means everything downstream is reused as-is, with no schema, API, or recap changes and no migration.

### 5. Extensibility: an open source registry (Open/Closed Principle)

The source abstraction is an **open registry**, not a closed set. Each source is a self-describing `IHighlightSource` that owns:

- its **identity** via a `SourceDescriptor` (`Id` + `DisplayName`) instead of a central `enum` discriminant;
- its own **detection** logic (a `Locate(userPath)` that resolves an explicit path or probes connected devices and reports the locations it checked);
- its **read** logic (`ReadAsync`).

The `HighlightSourceResolver` is constructed from the **injected collection** of sources and iterates them; it contains **no** per-source branching. DI registration order defines processing order. Adding a future highlight source (e.g. Readwise, a web export, another e-reader) therefore requires only: implement `IHighlightSource` and register it once in the DI container — no edits to the resolver, the import workflow, the command surface, or any enum.

**Rationale**: This applies the Open/Closed Principle — open for extension (new sources), closed for modification (no shared file changes per source). A central `enum` would force every new source to edit shared code and invites exhaustive `switch` statements that become parallel edit points; a self-describing descriptor, used only as a label and never branched on, keeps source-type knowledge at the source boundary. The registry shape costs essentially nothing now — the same detection code lives inside each source instead of inside the resolver — but turns future integrations into "implement and register". Dynamic/external plugin loading (assembly scanning, third-party plugin DLLs) is explicitly **not** adopted (YAGNI): sources are in-process and compile-time, discovered via DI. This extensibility model is a first-class promise of the architecture and must be documented for future integrators (`ARCHITECTURE.md`) and contributors (`CONTRIBUTING.md`).

## Consequences

- **New dependency**: the CLI gains a SQLite reader (`Microsoft.Data.Sqlite`) to read `KoboReader.sqlite`.
- **Open source registry**: `IHighlightSource` + `SourceDescriptor` + a DI-injected `HighlightSourceResolver` let the Kindle and Kobo readers feed the same pipeline with no per-source branching; a third source is "implement `IHighlightSource` and register it once". This model is documented for future integrations (`ARCHITECTURE.md`) and contributors (`CONTRIBUTING.md`).
- **Multi-source import**: when both devices are connected, both are imported in one run with per-source failure isolation. The resolver returns all detected sources; the import workflow loops over them, aggregating outcomes and reporting any per-source error without aborting the rest.
- **Zero changes** to server, storage, scheduler, recap composition, or the database schema.
- **No new delivery channel**: Kobo recaps depend on the regular email channel (feature 009) being configured. Users who want on-device reading must continue to sideload manually.
- **Locked-file handling**: the reader must copy the database before opening it, and tolerate store vs sideloaded `VolumeID` formats, soft-deleted (`Hidden`) rows, and UTF-8 content.
- **Auto-detection** must fail with an actionable error when neither a `My Clippings.txt` nor a `.kobo/KoboReader.sqlite` is found at the given path.
- **Future delivery**: Dropbox / Google Drive EPUB-to-device sync can be added later as a separate feature and ADR if a native on-device experience is desired.
- A sample `KoboReader.sqlite` fixture is added under `docs/examples/` for tests and documentation.
