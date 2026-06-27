# Feature Specification: Kobo Integration

**Feature Branch**: `016-kobo`
**Created**: 2026-06-14
**Status**: Draft
**ADR**: [ADR-008 — Kobo Integration: `KoboReader.sqlite` Source, Import-Only Delivery](../../docs/adr/008-kobo-reader-sqlite-source.md)
**Input**: User request: "Add Kobo e-readers as a second highlight source. A Kobo owner connects their device via USB and runs `relego sync`, exactly like a Kindle user. Highlights are read from `<KOBO_DRIVE>/.kobo/KoboReader.sqlite`. The feature is import-only: Kobo has no email-to-device address, so recaps reach Kobo users through the existing regular email channel (feature 009 / ADR-007). The Kobo reader emits the same parsed structures as the Kindle parser, so deduplication, grouping, the sync API, server storage, the scheduler, and recap composition are all unchanged."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Import Highlights and Notes from a Kobo Device (Priority: P1)

A Kobo user connects their e-reader via USB and runs the `relego sync` command, pointing it at the mounted Kobo drive. The system locates the `KoboReader.sqlite` database inside the device's `.kobo` folder, reads the stored highlights and notes, and extracts structured highlight data including book title, author, highlight text, and the date each highlight was created. Notes the user wrote on passages are imported alongside highlights and appear in recaps the same way Kindle notes do — prefixed with `[my note]` — so a recap reads identically regardless of which device the highlight came from. The user sees a summary of how many highlights were successfully imported — identical to the summary a Kindle user sees.

**Why this priority**: This is the foundational capability of the feature. Without reading highlights and notes from the Kobo database, nothing else (deduplication, sync) is possible for Kobo owners. It is the Kobo equivalent of Kindle's `My Clippings.txt` parsing, and cross-source note consistency is the core promise of treating Kobo as an equal source: notes are common on Kobo, and inconsistent handling would make recaps feel different depending on device.

**Independent Test**: Can be fully tested by providing a sample `KoboReader.sqlite` fixture and verifying that all valid highlights and notes are extracted with the correct book/author/text/date associations, that note text begins with the `[my note]` prefix matching the Kindle parser exactly, and that the output is emitted in the same structure the Kindle parser produces.

**Acceptance Scenarios**:

1. **Given** a `KoboReader.sqlite` database containing 40 highlights across 4 books, **When** the reader processes the database, **Then** all 40 highlights are extracted with the correct book title, author, highlight text, and creation date.
2. **Given** a Kobo database with highlights, notes (`Type = note`), and plain bookmarks (`dogear` / text-less rows), **When** the reader processes the database, **Then** highlights are extracted as-is, notes are extracted with a `[my note]` prefix prepended to their text, and bookmarks are skipped.
3. **Given** a Kobo database where some rows are soft-deleted (`Hidden = 'true'`), **When** the reader processes the database, **Then** the hidden rows are excluded from the output and only active highlights are imported.
4. **Given** a Kobo database whose file on the device is locked or mounted read-only, **When** the reader processes the device, **Then** the reader copies the database to a temporary file first and reads from the copy without modifying the device.
5. **Given** a Kobo database row with `Type = note` and annotation text, **When** the reader processes it, **Then** the emitted highlight text is the note content prefixed with `[my note]`.
6. **Given** a mix of highlight rows and note rows for the same book, **When** the reader processes the database, **Then** both appear under the same book group with notes distinguished only by the `[my note]` prefix and no separate type field.
7. **Given** the imported output, **When** it flows through the existing deduplication, grouping, and sync pipeline, **Then** no downstream component requires Kobo-specific handling to process the highlights or notes correctly.

---

### User Story 2 — Auto-Detect Source and Import From All Connected Devices (Priority: P1)

A user runs the single `relego sync` command and points it at a connected device or a path, without specifying which kind of device it is. The system inspects the location, determines whether it is a Kindle source (a `My Clippings.txt` file) or a Kobo source (a `.kobo/KoboReader.sqlite` database), and processes it with the matching reader — never requiring the user to declare a device type or pass a source-type flag. When both a Kindle and a Kobo are connected at the same time (an uncommon but valid setup), the single command imports highlights from **both** devices in one run; the processing order does not matter, and if importing from one device fails (e.g. a corrupt database), the system continues importing from the other and reports the failure rather than aborting the entire sync.

**Why this priority**: Auto-detection is what makes Kobo a first-class, equal source. It preserves the single uniform `relego sync` command across both device families and is required for User Story 1 to be reachable without new command syntax, so it must ship together with the Kobo reader. Supporting two simultaneously-connected devices is itself an edge case — most users own a single e-reader — but when it happens, silently importing only one device (or aborting the whole run on a single failure) would lose the user's highlights, so the behavior must be explicit and reliable.

**Independent Test**: Point `sync` at a Kindle path, a Kobo path, and an invalid path and verify each is routed to the correct reader (or rejected) without any source-type flag; then point it at a location where both a `My Clippings.txt` and a `.kobo/KoboReader.sqlite` are present and verify highlights from both are imported in one run, that making one source fail still imports the other and reports the failure, and that the combined result is order-independent.

**Acceptance Scenarios**:

1. **Given** a path containing a `My Clippings.txt` file, **When** the user runs `relego sync` against it, **Then** the system detects a Kindle source and uses the Kindle parser.
2. **Given** a path containing a `.kobo/KoboReader.sqlite` database, **When** the user runs `relego sync` against it, **Then** the system detects a Kobo source and uses the Kobo reader.
3. **Given** a path containing neither a `My Clippings.txt` nor a `.kobo/KoboReader.sqlite`, **When** the user runs `relego sync` against it, **Then** the command fails with an actionable error that explains which sources were looked for and where, and does not crash.
4. **Given** a directory or device, **When** the system resolves it, **Then** each detected source reports the concrete file to read and its display name, with no source-type flag required from the user.
5. **Given** both a Kindle and a Kobo source are detected, **When** the user runs `relego sync`, **Then** highlights from both sources are imported in a single run and the summary reports each source's result.
6. **Given** both sources are detected and one fails to read (e.g. a corrupt Kobo database), **When** sync runs, **Then** the other source is still imported successfully and the failed source is reported with an actionable error (the run is not aborted).
7. **Given** both sources are detected, **When** sync runs, **Then** the combined imported set is the same regardless of which source is processed first (order independence).
8. **Given** a Kobo user who has imported highlights and configured `delivery_email` (feature 009 / ADR-007), **When** a recap is scheduled, **Then** it is delivered through the existing regular email channel with no Kobo-specific delivery code — confirming the import-only scope end to end.

---

### Edge Cases

- What happens when the `KoboReader.sqlite` database is present but contains no `Bookmark` rows (a device with no highlights)?
- How does the reader handle a row whose `VolumeID` does not match any `content` row (orphaned highlight with no book metadata)?
- What happens when `content.Attribution` (author) is null or empty for a book?
- How does the reader handle store-purchased vs sideloaded books, whose `VolumeID` / `ContentID` formats differ?
- What happens when the database file is corrupt or not a valid SQLite file despite being named `KoboReader.sqlite`?
- How does the reader handle the temporary-copy step when the system temp directory is unavailable or the device is unexpectedly disconnected mid-read?
- What happens with UTF-8 content containing CJK characters, diacritics, or RTL scripts in titles, authors, highlight text, or notes?
- What happens when the same highlight text exists for the same book with different `DateCreated` values (re-highlighted at a different time)?
- How does the reader handle a very large Kobo library (tens of thousands of highlights)?
- What happens when a `Bookmark` row has `Type = note` but an empty annotation, or `Type = highlight` but null `Text`?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST add Kobo e-readers as a second highlight source usable through the existing `relego sync` command, with the same connect-via-USB workflow as Kindle.
- **FR-002**: System MUST read Kobo highlights exclusively from `<KOBO_DRIVE>/.kobo/KoboReader.sqlite`. Adobe-style `.annot` annotation files are explicitly out of scope.
- **FR-003**: System MUST read highlights and notes from the `Bookmark` table joined to the `content` table on `Bookmark.VolumeID = content.ContentID`, extracting `content.Title` (title), `content.Attribution` (author), `Bookmark.Text` (highlight text), `Bookmark.Annotation` (note text), `Bookmark.Type` (entry type), and `Bookmark.DateCreated` (creation date).
- **FR-004**: System MUST treat `Type = note` rows as highlights with a `[my note]` prefix prepended to their text, mirroring the Kindle parser's note handling exactly.
- **FR-005**: System MUST skip `dogear` rows and text-less rows (plain bookmarks with no highlight content), mirroring how Kindle bookmarks are skipped.
- **FR-006**: System MUST skip soft-deleted rows where `Bookmark.Hidden` indicates the row is hidden.
- **FR-007**: System MUST copy `KoboReader.sqlite` to a temporary file before opening it, and read only from the copy, so a locked or read-only device file does not block the import and the device is never modified.
- **FR-008**: System MUST auto-detect, from the provided path or device, whether the source is a Kindle source (`My Clippings.txt`) or a Kobo source (`.kobo/KoboReader.sqlite`), without requiring the user to pass a source-type flag.
- **FR-009**: System MUST fail with an actionable error when neither a `My Clippings.txt` nor a `.kobo/KoboReader.sqlite` source is found at the given location, stating what was looked for and where.
- **FR-010**: System MUST emit Kobo highlights using the same parsed book/highlight/result structures the Kindle parser produces, so that deduplication, grouping, the sync API, server storage, the scheduler, and recap composition operate unchanged and remain source-agnostic.
- **FR-011**: System MUST expose highlight sources through an open abstraction (`IHighlightSource`) where each source is **self-describing** — it carries its own descriptor (a stable id and a display name, not a shared enum) and owns its own detection logic — and the source resolver MUST operate over the injected set of registered sources **without per-source branching**, so a new source can be added by implementing the abstraction and registering it once (no edits to the resolver, import workflow, command surface, or any enum). External/dynamic plugin loading is out of scope; sources are in-process and registered via dependency injection.
- **FR-012**: System MUST handle a Kobo database that contains no importable highlights by returning an empty result without errors.
- **FR-013**: System MUST skip individual malformed or orphaned rows (e.g., a highlight whose `VolumeID` has no matching `content` row) and continue importing the remaining valid rows, logging skipped rows as warnings.
- **FR-014**: System MUST NOT add any new delivery channel for Kobo. Kobo recaps MUST be delivered through the existing regular email channel (`delivery_email`, feature 009 / ADR-007); no cloud-folder (Dropbox / Google Drive) delivery is added.
- **FR-015**: System MUST make no changes to the server, database schema, sync API contract, scheduler, or recap composition as part of this feature.
- **FR-016**: System MUST include a sample `KoboReader.sqlite` fixture under `docs/examples/` for use in tests and documentation.
- **FR-017**: When more than one highlight source is detected at sync time (e.g. a Kindle and a Kobo are both connected), the system MUST import from **all** detected sources in a single run; the import order is not significant.
- **FR-018**: A failure importing one source MUST NOT prevent importing the others. Each per-source failure MUST be reported to the user with an actionable message while the remaining sources continue to import (per-source failure isolation).

### Key Entities

- **Kobo Source**: A connected Kobo device or a path resolving to a `.kobo/KoboReader.sqlite` database. The unprocessed input for the Kobo reader.
- **Bookmark Row**: A single raw row from the Kobo `Bookmark` table, carrying highlight or note text, an entry type, a creation date, a hidden flag, and a `VolumeID` linking it to a book. Represents unprocessed input before normalization.
- **Content Row**: A row from the Kobo `content` table providing book metadata (title, author) for a given `ContentID`.
- **Source Abstraction**: The common interface (`IHighlightSource`) through which every reader feeds the downstream pipeline, isolating source-type knowledge to the reader boundary. The set of sources is **open** — new sources are added by implementing the interface and registering them, with no central enum or resolver edits.
- **Source Descriptor**: A source's identity — a stable id plus a human-readable display name — owned by the source itself and used only for reporting/logging. Replaces a central source-type enum so the abstraction stays open for extension.
- **Source Resolution**: The result of resolving an input path/device against the registered sources: the set of sources to import from (one source normally, or every detected source when several devices are connected), or an actionable not-found error listing every probed location.
- **Parsed Highlight**: A normalized highlight or note (notes prefixed with `[my note]`) associated with exactly one book — identical in shape to the Kindle parser's output.
- **Parsed Book**: A book identified by title and author, containing one or more parsed highlights — identical in shape to the Kindle parser's output.
- **Parse Result**: The complete normalized output of importing a source, consumed by the existing deduplication, grouping, and sync pipeline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A Kobo user can run `relego sync` against their connected device and import their highlights with no additional flags or configuration beyond what a Kindle user provides.
- **SC-002**: All valid highlights and notes in a `KoboReader.sqlite` database are imported with 100% accuracy — correct title, author, text, and date — with no valid row missed and no skipped row (bookmark, hidden, orphaned) included.
- **SC-003**: Notes from a Kobo device appear in recaps with the `[my note]` prefix, indistinguishable from Kindle notes.
- **SC-004**: `relego sync` correctly routes a Kindle path, a Kobo path, and an invalid path to the right reader or to an actionable error, with zero misroutes and no source-type flag required.
- **SC-005**: Kobo-sourced highlights flow through deduplication, grouping, the sync API, storage, scheduling, and recap composition with zero source-specific branches in any downstream component.
- **SC-006**: A recap delivered to a Kobo user's `delivery_email` is produced entirely by the existing email-delivery path, with no Kobo-specific delivery code executed.
- **SC-007**: The import never modifies the device's `KoboReader.sqlite` — verified by confirming the on-device file is byte-identical before and after a sync.
- **SC-008**: A Kobo library with 10,000 highlights is imported, normalized, deduplicated, and grouped within 5 seconds.
- **SC-009**: When both a Kindle and a Kobo source are connected, `relego sync` imports highlights from both in a single run, and a failure reading one source never prevents the other from importing — the failure is reported and the successful source still completes.
- **SC-010**: A new highlight source can be added by implementing `IHighlightSource` and registering it once in the DI container, with no changes to the `HighlightSourceResolver`, the import workflow, the command surface, or any enum.

## Assumptions

- The Kobo device, when connected via USB, mounts as a drive whose root contains a `.kobo` folder with `KoboReader.sqlite`. This layout is stable across current Kobo models.
- `KoboReader.sqlite` uses UTF-8 text and the `Bookmark` / `content` schema described in ADR-008. The `Bookmark.Hidden` column stores a string flag (`'true'` / `'false'`); the reader treats any "hidden" value as a skip.
- `Bookmark.Type` values of interest are `highlight`, `note`, and `dogear`; only `highlight` and `note` produce output, and `note` rows receive the `[my note]` prefix.
- The join key `Bookmark.VolumeID = content.ContentID` resolves book metadata for both store-purchased and sideloaded titles; rows with no matching `content` row are skipped as orphaned.
- Copying the database to a temporary file before reading is sufficient to avoid device-lock and read-only issues; the temporary copy is removed after the read.
- Deduplication, grouping, and the sync payload are unchanged from the Kindle path because the Kobo reader emits identical structures; date metadata is informational and not used for deduplication or grouping (consistent with the Kindle parser).
- The Kobo reader is client-side logic that runs locally in the CLI during sync, with no server, database, or network dependency.
- Delivery for Kobo users relies entirely on the regular email channel (feature 009 / ADR-007) being configured by the user; this feature adds no delivery code.
- Auto-detection between sources is reliable because the two sources are unambiguous to distinguish (file name, presence of a `.kobo` folder, SQLite file signature). When two devices are connected simultaneously, each maps to exactly one source and both are imported independently (no precedence/tie-break is needed).
- The sample `KoboReader.sqlite` fixture under `docs/examples/` is small and synthetic, containing representative highlight, note, dogear, hidden, and orphaned rows for test coverage.

## Out of Scope

- Reading Adobe-style `.annot` XML annotation files under `Digital Editions/Annotations/`.
- Any new on-device delivery channel for Kobo, including Dropbox or Google Drive cloud-folder EPUB sync (may be added later as a separate feature and ADR).
- Changes to the server, database schema, sync API contract, scheduler, or recap composition.
- A source-type flag or any change to the `relego sync` command surface beyond auto-detection.
- An EPUB-by-email path for Kobo (Kobo has no email-to-device address).
- Dynamic or external plugin loading for highlight sources (assembly scanning, third-party plugin DLLs); sources are in-process, compile-time, and registered via dependency injection.
