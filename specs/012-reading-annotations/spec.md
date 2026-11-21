# Feature Specification: Reading Annotations, Notes, Chapters, and Tags

**Feature Branch**: `fr02-reading-annotations`

**Created**: 2026-04-18

**Status**: Revision 1 — spec, plan and tasks approved 2026-11-21; implementation not started

**Input**: User description: "Implement FR-02 of the V1 PRD: preserve highlight text, notes, title/author, source location and timestamps; capture chapters from explicit heading annotations; keep missing data unknown; keep legacy records working." Extended during specification to cover the rest of the annotation vocabulary a reader may already be using: notes, chapter markers, stitched highlights and inline tags.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - My notes stay with the passage they belong to (Priority: P1)

A reader highlights a passage and adds a thought about it. Today the thought arrives as a separate item with a visible `[my note]` marker in front of it, adrift from the passage it explains. After this change the reader sees one annotation: the passage, and directly beneath it the note they wrote, presented as a note rather than as another quote.

**Why this priority**: Notes are the most common form of reader commentary, and today they are presented as if they were highlights. This is the core of FR-02 and delivers value on its own even if nothing else in this feature ships.

**Independent Test**: Import a reading file that contains a highlight and a note attached to it, open the library, and confirm the note is displayed under that highlight with the marker gone.

**Acceptance Scenarios**:

1. **Given** a source where a note is reported at the same position as a highlight, **When** the import runs, **Then** the note is attached to that highlight and is no longer a separate entry in the library.
2. **Given** a note that cannot be matched to any highlight in the source, **When** the import runs, **Then** the note is still kept as its own annotation and remains visible in the library.
3. **Given** a note written over several lines, **When** the import runs, **Then** its line breaks and wording are preserved exactly.
4. **Given** an already-imported book, **When** the same source is imported again unchanged, **Then** no annotation is duplicated and each note stays attached to the same highlight.
5. **Given** a note attached to a highlight, **When** that highlight is chosen for a recap, **Then** the note is delivered together with the passage.

---

### User Story 2 - Chapters from heading markers (Priority: P1)

A reader who cares about structure highlights the title of a section and adds the note `.h1` (or `.h2`, `.h3`) instead of typing commentary. Those marked passages become the book's chapters, and everything read afterwards is understood to belong to the most recent chapter. The reader can also write a section summary after the marker, on a new line, and it appears beneath the heading. Ordinary highlights keep their place inside that structure and show the section they belong to instead of a bare page number.

**Why this priority**: Chapter context is what makes a recap feel like the book rather than a random pile of quotes, and the marker is the only way chapter data is captured at all. Without this story nothing else in the feature can work.

**Independent Test**: Import a book that contains `.h1` and `.h2` marked passages interleaved with normal highlights, and confirm each highlight is associated with the correct chapter and nesting level, and each heading shows its own summary note.

**Acceptance Scenarios**:

1. **Given** a book whose annotations include one passage noted `.h1` and a later passage noted `.h2`, **When** the import runs, **Then** the second chapter is nested under the first and neither marker is shown as the reader's commentary.
2. **Given** a heading note written as `.h1` followed by a line break and a summary sentence, **When** the reader views the book, **Then** the passage is shown as a heading with the summary sentence beneath it as the chapter's note.
3. **Given** a book with no heading markers, **When** the import runs, **Then** every annotation of that book is reported as having an unknown chapter and none is dropped or hidden.
4. **Given** a passage noted `.h2` that appears before any `.h1` in the book, **When** the import runs, **Then** the chapter exists at its own level with no parent, it is shown as a heading in the annotation list, and its highlights remain usable.
5. **Given** a book with chapters, **When** the reader looks at any highlight in it, **Then** the highlight shows the section it belongs to instead of only a page or location number.

---

### User Story 3 - Browse a book by chapter, including the unknown chapter (Priority: P2)

Looking at a single book, the reader sees a table of contents built from their markers, can jump to a chapter's annotation list in one click, and can also narrow the list to a single chapter - including the annotations that sit before the first chapter or in a book that has no chapters at all.

**Why this priority**: The structure captured in Story 2 is only useful when it can be navigated. It builds on Story 2 and delivers value on its own once chapter data exists.

**Independent Test**: Open a book that has at least two chapters, jump to one chapter from the contents, then filter to it, and confirm only that chapter's annotations are shown; filter to the unknown chapter and confirm only the unassigned ones are shown.

**Acceptance Scenarios**:

1. **Given** a book with at least one `.h1` marker, **When** the reader opens that book, **Then** a table of contents is shown in reading order with the correct nesting.
2. **Given** the table of contents is visible, **When** the reader picks a chapter, **Then** the book view moves to that chapter's annotations.
3. **Given** a chapter is filtered, **When** the list refreshes, **Then** only annotations belonging to that chapter are displayed.
4. **Given** annotations that appear before the first chapter marker, **When** the reader selects the unknown chapter, **Then** those annotations are listed.
5. **Given** a book whose only markers are `.h2` or `.h3`, **When** the reader opens it, **Then** the passages are shown as headings but no table of contents is offered.
6. **Given** a book with no heading markers, **When** the reader opens it, **Then** no table of contents is offered, all annotations remain visible, and no chapter is invented.

---

### User Story 4 - Existing library upgrades without loss (Priority: P2)

A reader who has been using Relego before this change upgrades and finds the same annotations, the same weights and exclusions, and their old notes now presented as notes rather than as quoted passages with a marker.

**Why this priority**: The value of Stories 1-3 is destroyed if upgrading discards or reinterprets existing data. FR-02 explicitly forbids destructive reinterpretation of existing records.

**Independent Test**: Start from a library created by the previous release, upgrade, and confirm annotation count, weights, exclusions and delivery history are unchanged while old notes are shown as notes.

**Acceptance Scenarios**:

1. **Given** an annotation whose text begins with the legacy note marker, **When** the upgrade runs once, **Then** it becomes an annotation that carries the text after the marker as its note, and the marker is no longer visible anywhere to the reader.
2. **Given** any pre-existing annotation, **When** the upgrade runs, **Then** its weight, excluded state, delivery count, book, author and dates are preserved unchanged.
3. **Given** the upgrade has already been applied, **When** the system restarts, **Then** it does not run again and does not modify the same records a second time.
4. **Given** the previous release's data, **When** the upgrade completes, **Then** every annotation that was visible before is still visible.

---

### User Story 5 - Accurate provenance for every annotation (Priority: P3)

Every annotation can tell the reader where it came from in the book and when it was added, and says so honestly when the source did not provide that information.

**Why this priority**: This is what makes chapters orderable and recaps trustworthy, but it stays invisible until later milestones consume it.

**Independent Test**: Import annotations with and without positions and dates, and confirm the known ones are shown while the missing ones are reported as unknown instead of being filled in.

**Acceptance Scenarios**:

1. **Given** a source that reports a position and a date for a highlight, **When** the import runs, **Then** both are stored and visible on that annotation.
2. **Given** a source that does not report a position or a date, **When** the import runs, **Then** the annotation is kept and the missing values are reported as unknown, never derived from the import time.
3. **Given** a book whose author is not reported by the source, **When** the reader views it, **Then** the author is shown as unknown rather than as a substituted name.

---

### User Story 6 - Stitch related passages into one (Priority: P2)

A reader marks a passage with `.c1`, later marks a related passage elsewhere in the same book with `.c2`, and another with `.c3`. Instead of reading three disconnected fragments, the reader sees one stitched passage in the order the passages were read, joined by an ellipsis, keeping each fragment's own position.

**Why this priority**: Stitching is how a reader reconstructs an argument that is spread across a book. It builds on annotation identity and source order from the earlier stories and is useful on its own.

**Independent Test**: Import a book containing a `.c1`, a `.c2` and an unrelated highlight, and confirm the two marked passages appear as a single stitched passage joined by an ellipsis while the unrelated highlight is untouched.

**Acceptance Scenarios**:

1. **Given** a book with a `.c1`-marked passage and a later `.c2`-marked passage, **When** the import runs, **Then** the reader sees one stitched passage containing both fragments in reading order, joined by an ellipsis.
2. **Given** a `.c2`-marked passage that appears before any `.c1` in the book, **When** the import runs, **Then** no stitching happens and the passage keeps its own annotation with the marker preserved as a visible tag.
3. **Given** two separate runs of `.c1` markers in one book, **When** the import runs, **Then** exactly two stitched passages are produced, each starting at its own `.c1`.
4. **Given** a stitched passage, **When** the reader removes one fragment's marker on the device and imports again, **Then** the stitching reflects the change and no fragment is lost.
5. **Given** the reader views a stitched passage, **When** it is displayed in the web portal, **Then** the fragments remain individually readable and each keeps its own position and note.

---

### User Story 7 - File passages under their own tags (Priority: P2)

A reader who annotates with a word instead of a sentence - `.prob`, `.important`, `.review` - gets those words treated as tags on the passage rather than as stray comments. Tags are visible on the annotation, browsable and filterable, and a tag can sit alongside an ordinary note in the same note field.

**Why this priority**: Tagging is how a reader organises annotations for later reuse, and today those words are shown as meaningless commentary. It depends on notes being modelled correctly but needs nothing else from this feature.

**Independent Test**: Import a book containing passages noted `.prob`, `.prob .review` and `.prob followed by a real comment`, and confirm the first two gain tags with no stray comment, while the third gains a tag plus its comment preserved exactly.

**Acceptance Scenarios**:

1. **Given** a passage whose note is a single dot-prefixed word, **When** the reader views the annotation, **Then** the word is presented as a tag on that annotation and not as commentary.
2. **Given** a passage whose note is several dot-prefixed words separated by spaces, **When** the reader views the annotation, **Then** each word becomes a tag on that annotation.
3. **Given** a passage whose note is a tag followed by more text, **When** the reader views the annotation, **Then** the tag is separated from the remaining text and the remaining text is preserved as the annotation's note.
4. **Given** annotations carrying tags, **When** the reader filters a book's list by a tag, **Then** only annotations carrying that tag are shown.
5. **Given** a book with no tags, **When** the reader opens it, **Then** no tag filtering is offered for that book and every annotation remains visible.
6. **Given** a passage that already carried a tag, **When** the reader browses it from the CLI, **Then** the tag and the note are reported for that passage as distinct values.

---

### User Story 8 - The same structure from the web portal and the CLI (Priority: P3)

Whichever surface the reader uses, annotations carry the same notes, headings, stitched passages and tags, and can be narrowed by chapter and by tag in the same way.

**Why this priority**: Two surfaces that disagree about what an annotation is would fragment the feature. It matters once the earlier stories work and is verified last.

**Independent Test**: Retrieve annotations for a book that has chapters, notes and tags, from the web portal and from the CLI, and confirm both report the same note, chapter context, stitched grouping and tags for the same annotation.

**Acceptance Scenarios**:

1. **Given** a book with chapters, notes and tags, **When** the same annotations are read through the web portal and through the CLI, **Then** both report the same note, chapter context, stitched grouping and tags for each annotation.
2. **Given** a chapter in a book, **When** the reader narrows annotations by that chapter through either surface, **Then** both return the same set of annotations.
3. **Given** a tag in use, **When** the reader narrows annotations by that tag through either surface, **Then** both return the same set of annotations.

---

### Edge Cases

- A note whose position does not match any highlight in the same source stays a standalone annotation and is shown as commentary without an accompanying passage.
- A note is repeated for the same position because the reader edited it on the device; the most recent version is kept and the superseded version is reported as a duplicate instead of accumulating.
- A note consists only of whitespace; it counts as no note and is not displayed as an empty comment.
- A heading marker appears on a passage that is not the first in the book; annotations preceding it form the unknown chapter.
- A heading marker is written with surrounding whitespace or upper-case letters; it is still recognised as a heading marker.
- A heading marker carries its summary on the same line instead of after a line break; the summary is still treated as the chapter's note rather than as part of the marker.
- A heading marker sits on a passage with no text; no chapter is created because there is no title to show, and the annotation is kept.
- A note begins with a dot that is not a heading marker or a stitching marker (for example `.probability`); it is interpreted as a tag, so the reader's word is never shown as stray commentary and never dropped.
- A note is a dot with no word after it; it is ordinary commentary, not a tag.
- A stitching marker appears in a book with a single member; the stitched passage is that one fragment, presented without a trailing ellipsis.
- Stitched fragments are not adjacent in the book; they are still joined in reading order, with unrelated annotations left in place between them.
- A fragment of a stitched passage is excluded from recaps; the remaining fragments still form a passage and nothing is deleted.
- Two fragments carry identical passage text in the same book; both are stitching members, and neither is collapsed by the duplicate rule.
- A note carries the same tag twice; the tag is applied once.
- A tag differs only by letter case from another tag in the library; both are shown as the reader wrote them and filtering by either finds both.
- A tag sits on a passage whose note also continues afterwards on a second line; the tag and the remaining note are both preserved.
- Two chapters share the same title; they stay distinct chapters in source order and are never merged.
- A heading marker is added to an already-imported highlight; the existing annotation becomes that chapter and no second record is created.
- A highlight's marker changes level later (for example `.h2` corrected to `.h3`); the chapter is re-levelled and its descendants are re-parented.
- Annotations arrive in a different order than they appear in the book (a later sync supplies earlier pages); the chapter structure is recomputed from source order, not from arrival order.
- Annotations in one book report positions in different units (page versus location); ordering falls back to the reported date and then to arrival order for those entries.
- A book is re-imported after its heading markers are removed from the device; affected annotations fall back to the nearest remaining chapter or to unknown, and none is deleted.
- A chapter contains annotations but no longer has a heading because the marker was deleted; its annotations stay reachable through the unknown chapter rather than disappearing.
- One detected source fails while another succeeds; the successful source's outcome is still reported and the failed source is reported separately with an actionable message.
- A reading file is unreadable, locked or absent; the import reports the failure and does not modify any device data.

## Requirements *(mandatory)*

### Functional Requirements

**Existing import behaviour that must be preserved**

- **FR-001**: System MUST keep both existing local import paths - reading-file import and device-database import - unchanged in how sources are located, selected and invoked, including automatic source detection.
- **FR-002**: System MUST treat every imported source as read-only; no imported device file may be modified, moved or deleted.
- **FR-003**: System MUST evaluate each detected source independently, so that a failure in one source never suppresses the reported outcome of another source in the same run.
- **FR-004**: System MUST report, per source, how many records were new, how many were recognised as duplicates, and why a source failed when it failed.

**Provenance and fidelity**

- **FR-005**: System MUST preserve passage text exactly as reported by the source.
- **FR-006**: System MUST store the position reported by the source for each annotation, and MUST report the position as unknown when the source does not provide one.
- **FR-007**: System MUST store the date reported by the source for each annotation, and MUST report it as unknown when the source does not provide one; the date the system learned about the record MUST be tracked separately and MUST NOT be presented as the reading date.
- **FR-008**: System MUST preserve the book title and author exactly as reported, and MUST present a missing title or author as unknown instead of substituting a generated value.
- **FR-009**: System MUST NOT synthesise any missing attribute - including chapter, position, date and author - from other attributes, from text content, or from the time the data was received.

**Notes**

- **FR-010**: System MUST attach a note to the annotation it annotates when the source relates the two, keeping the note text exactly as written, including its line breaks.
- **FR-011**: System MUST keep a note that cannot be related to any highlight as its own annotation that carries the note text and no passage text, and it MUST remain visible, searchable and selectable for recaps.
- **FR-012**: System MUST present an annotation that carries a note as one item - passage, then note directly beneath it - so the note is never rendered in passage styling and never displays the legacy `[my note]` marker.
- **FR-013**: When one position reports several versions of a note, System MUST keep the most recent version according to source information and MUST report the superseded versions as duplicates.
- **FR-014**: System MUST include note text when searching annotations.
- **FR-015**: System MUST treat a note that is empty or contains only whitespace as no note, without discarding the annotation it belongs to.

**Heading markers and chapters**

- **FR-016**: System MUST treat a note that starts with a heading marker - a period, the letter `h` in either case, and a digit from 1 to 3 - as a heading marker, ignoring whitespace before it, and MUST make the annotated passage the title of a chapter at that level.
- **FR-017**: System MUST NOT display the heading marker itself as the reader's commentary. When the note continues after the marker, the remaining text is the chapter's section note and MUST be preserved exactly and shown beneath the heading; when nothing follows, the chapter simply has no section note.
- **FR-018**: System MUST treat every note that does not begin with a dot-prefixed token as ordinary commentary.
- **FR-019**: System MUST interpret a note that begins with a reserved dot-prefixed token - a heading marker, a stitching marker, or one or more single-word tags - according to that token, and MUST NOT display the token as the reader's commentary. A note consisting only of a dot is ordinary commentary.
- **FR-020**: System MUST NOT create a chapter when the marked annotation has no passage text, because there is no title to show; the annotation itself is kept.
- **FR-021**: System MUST derive chapter hierarchy for a book from its heading-marker annotations using marker level and source order; every annotation belongs to the most recent heading-marker annotation at or before it in source order.
- **FR-022**: System MUST nest a chapter under the nearest preceding chapter of a shallower level, and MUST keep a chapter with no shallower predecessor at the top level.
- **FR-023**: System MUST order chapters by source order regardless of the order in which annotations arrive, and MUST recompute that order when earlier content is imported later.
- **FR-024**: System MUST present heading-marker annotations as headings in the annotation list - structurally distinct from quoted passages - with their section note beneath them.
- **FR-025**: System MUST show each annotation's chapter context, including its ancestor chapters when nested, in place of a bare page or location number, while keeping the raw source position stored and available.
- **FR-026**: System MUST offer a table of contents for a book when at least one level-1 heading marker exists, listing chapters in reading order with their nesting; when only deeper-level markers exist they are still presented as headings but no table of contents is offered, and no table of contents is offered for a book without markers.
- **FR-027**: System MUST treat annotations that precede the first heading marker, and every annotation of a book with no heading markers, as belonging to the unknown chapter - always reachable, never hidden by default, and never excluded from recap selection.
- **FR-028**: System MUST NOT infer chapters from passage text, position, length or any other signal, and MUST NOT create a chapter without an explicit heading marker.
- **FR-029**: System MUST keep heading-marker annotations as curated records with their weight, exclusion and delivery history, and MUST present them as a section's title rather than as a quoted passage when a delivery includes that section's annotations.

**Navigation**

- **FR-030**: Users MUST be able to move, from the table of contents, to a chapter's annotations inside the book view.
- **FR-031**: Users MUST be able to narrow a book's annotation list to one chapter, including the unknown chapter.
- **FR-032**: System MUST show, on each annotation, its note when present, its chapter context when known, and its position and date when known, and MUST visually distinguish headings from ordinary highlights.
- **FR-033**: System MUST expose annotation, note, chapter, heading, stitched-passage and tag information through the product's single annotation surface, so the shared list, the book view, search, delivery, the web portal and the CLI all read the same representation.

**Repeat imports and change handling**

- **FR-034**: System MUST identify an annotation by its book and passage text, independently of its note and of any marker it carries, so that adding or changing a note, heading marker, stitching marker or tag updates the existing annotation instead of creating another.
- **FR-035**: System MUST make repeated imports of unchanged data idempotent: no duplicate annotations, notes, chapters, stitched passages or tags, and no re-association of existing notes.
- **FR-036**: System MUST update an existing annotation instead of inserting a duplicate when the source reports changed content, such as a corrected passage, a corrected or added note, or a passage that gained, lost or changed a heading marker, stitching marker or tag.
- **FR-037**: System MUST recompute a book's chapter structure and stitched passages from the source order of its marked annotations after every import that affects that book, MUST re-parent descendants when a chapter marker's level changes, and MUST never delete annotations when a chapter or a stitched passage changes.

**All import paths agree**

- **FR-038**: System MUST normalise annotation, note and heading data from every ingestion path - reading file, device database, and any separately delivered sync or upload of highlights - into the same annotation shape, so a book behaves identically in the library and in recaps regardless of source.
- **FR-039**: System MUST preserve the ordering information supplied by every ingestion path so chapter hierarchy can be derived in reading order even when a source reports positions in a different form than another source.

**Stitched passages**

- **FR-040**: System MUST treat a note that starts with a stitching marker - a period, the letter `c` in either case, and a number - as a stitching marker rather than as commentary, ignoring whitespace before it, and MUST present the resulting stitched passage without the marker.
- **FR-041**: System MUST treat a passage marked with the first stitching level as the start of a stitched passage, and MUST append to it the passages marked with later stitching levels in reading order, joining the fragments with an ellipsis.
- **FR-042**: System MUST NOT stitch across books, MUST NOT stitch a fragment whose marker has no preceding first-level marker in the same book, and MUST start a new stitched passage at every first-level marker.
- **FR-043**: System MUST keep every fragment of a stitched passage individually stored with its own position, note and curation state, so removing a marker, changing a marker, or excluding a fragment never deletes a fragment and never affects the other fragments' records.
- **FR-044**: When a fragment of a stitched passage is excluded, System MUST omit that fragment from the stitched passage while keeping the remaining fragments stitched and delivered.
- **FR-045**: System MUST treat a stitched passage as one quoted item for display, search and delivery, while keeping its fragments individually inspectable.

**Inline tags**

- **FR-046**: System MUST interpret one or more leading single-word dot-prefixed tokens on a note as tags on that annotation, and MUST apply the same tag only once when it is repeated in the same note.
- **FR-047**: System MUST treat the text following the tags in a note as the annotation's note, preserved exactly, so a tag can coexist with commentary in the same note field.
- **FR-048**: System MUST present tags as tags rather than as commentary, MUST keep the reader's original spelling and letter case, and MUST NOT drop a tag whose meaning the product does not yet act on.
- **FR-049**: Users MUST be able to narrow a book's annotation list, and the annotation list as a whole, to one tag.
- **FR-050**: System MUST match tags without regard to letter case when filtering, and MUST treat two spellings that differ only by case as the same tag for filtering while displaying each annotation's tag as the reader wrote it.
- **FR-051**: System MUST include tag names when searching annotations.
- **FR-052**: System MUST make tag names available wherever annotations are consumed, so that listing, filtering and delivery read the same tags regardless of surface.

**Existing-data upgrade**

- **FR-053**: System MUST migrate records whose passage text begins with the legacy note marker exactly once, converting them into annotations that carry the remainder of the text as their note and presenting them as commentary rather than as passages, and MUST interpret that remainder like any other note, including reserved tokens it may contain.
- **FR-054**: System MUST preserve, for every pre-existing record, its curation state, delivery history, book, author, dates and identifiers, and MUST NOT reduce the number of visible records.
- **FR-055**: System MUST record that the one-time migration has been applied so restarts and later imports never repeat or extend it, and MUST NOT apply the legacy-marker rule to records received afterwards.
- **FR-056**: System MUST treat pre-existing records that do not match the legacy marker rule as ordinary annotations without a note, without altering their content.

### Key Entities

- **Annotation**: A record of something the reader marked in a book. Carries passage text (possibly empty when the annotation is a note without a passage), an optional note, an optional heading level from 1 to 3, an optional stitching level, the tags applied to it, an optional position, an optional reading date, a known-or-unknown author, its book, and the existing curation state and delivery history.
- **Note**: Commentary written by the reader, either attached to one annotation or standing alone when no passage could be related to it. May be several lines. Never displayed with the legacy marker, and never contains a marker or a tag it introduced.
- **Heading marker**: The dot-prefixed token at the start of a note that turns its annotation into a chapter title. Never displayed to the reader.
- **Stitching marker**: The dot-prefixed token, a period plus the letter `c` and a number, that joins its annotation to the stitched passage started by the first-level marker in the same book. Never displayed to the reader.
- **Stitched passage**: One or more annotations joined into a single quoted item, ordered as read and separated by an ellipsis. Each fragment keeps its own position, note, tags and curation state, so the passage is a derived reading of its fragments rather than a replacement for them.
- **Tag**: A single word the reader attached to an annotation by writing it after a period in the note. Preserves the reader's spelling, is matched without regard to letter case, and is available for display, filtering and delivery.
- **Chapter**: A structural section of a book derived from a heading-marker annotation. Has a title taken from the marker annotation's passage, a level from 1 to 3, an optional parent chapter, an optional section note, source order, and the annotations it contains.
- **Chapter context**: The chain of a chapter and its ancestors that an annotation displays as its location in the book, used instead of a bare position number.
- **Unknown chapter**: The grouping for annotations not preceded by any heading marker, and for every annotation of a book whose source carried no markers, including annotations whose chapter lost its marker. Always available, never a placeholder that hides data.
- **Source position**: The location or range reported by the source for an annotation, together with the form the source used for it, or unknown.
- **Reading date**: The date reported by the source for an annotation, or unknown. Distinct from the date the system learned about the record.
- **Legacy note record**: A pre-existing record from the earlier release that encodes a note inside the passage text with the legacy marker, and therefore needs the one-time migration rule.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the passages, notes, positions and reading dates present in a source appear in the library after import, and 0% of the missing values are filled with generated substitutes.
- **SC-002**: In a source containing highlights with attached notes, 100% of the notes are attached to the correct passage on first import, and 0 legacy markers are visible anywhere in the product.
- **SC-003**: For a book with heading markers, 100% of annotations are assigned to the chapter a reader would expect from reading order, 100% of heading markers with a summary produce both a chapter and a visible section note, and 0 chapter titles contain marker text.
- **SC-004**: A table of contents is offered for 100% of books with at least one level-1 heading marker, and for 0 books without one, including books that only have deeper-level markers.
- **SC-005**: On a book with chapters, 100% of annotations display their chapter context, and 0 annotations display a chapter they were not derived from.
- **SC-006**: Adding a heading marker to an already-imported highlight creates 0 new records and produces exactly 1 re-levelled chapter tree.
- **SC-007**: Re-importing an unchanged source adds 0 records, changes 0 note attachments and changes 0 chapter assignments.
- **SC-008**: Upgrading a library created by the previous release changes 0 weights, 0 exclusions and 0 delivery counts, and removes 0 visible records.
- **SC-009**: A reader reaches any single chapter's annotations for a book, including the unknown chapter, in two interactions or fewer from the book view.
- **SC-010**: Every imported device file is byte-for-byte unchanged after import, verified on 100% of imports.
- **SC-011**: When one of several sources fails, 100% of the remaining sources still report their outcomes in the same run.
- **SC-012**: 100% of passages marked for stitching appear in exactly one stitched passage, joined in reading order, and 0 markers are visible anywhere to the reader.
- **SC-013**: Re-importing a book whose stitching markers were removed restores 100% of the affected passages as individual annotations and loses 0 records.
- **SC-014**: 100% of dot-prefixed words written as tags appear as tags on their annotation, and 0 of them appear as commentary.
- **SC-015**: A reader narrows an annotation list to one tag or one chapter in two interactions or fewer, and the same narrow result is returned by the web portal and by the CLI.
- **SC-016**: For every annotation, the note, chapter context, stitched grouping and tags reported by the web portal and by the CLI are identical.

## Assumptions

- The anchor for relating a note to its passage is the position reported by the source: a note and a highlight reported at the same position in the same book belong together. Sources that report no positions keep notes as standalone annotations.
- A heading marker is recognised as a prefix rather than as the whole note, because a marker followed by a new line and a summary is the documented way to write a section summary. Whitespace and letter case around the marker are ignored.
- Reserved dot-prefixed tokens are recognised by their shape, not by a fixed list of names, so a tag the product does not act on is still shown as the reader wrote it rather than being hidden or silently reinterpreted.
- Only levels 1 to 3 define chapters, and only a level-1 marker produces the table of contents. Deeper-level markers are still presented as headings and their annotations still carry chapter context.
- Stitched passages are derived from their fragments rather than replacing them, so weights, exclusions and delivery history stay attached to the fragment records the reader already curated.
- Only the first stitching level starts a passage and later levels append; a fragment whose marker has no preceding first-level marker in the same book is left alone rather than guessed into a group.
- A tag is a single word after a period; a period followed by a space, or by nothing, is ordinary commentary, because there is no word to treat as a tag.
- Tags have no interpretation beyond organisation in this feature: no automatic exclusion, weighting, recap priority or delivery effect is attached to a tag name.
- Chapter structure is derived rather than authored: it is recomputed whenever a book's annotations change and is not reader-editable content in this feature.
- A heading-marker annotation stays a normal annotation for curation purposes, so existing weights and exclusions keep working; it is presented as a section title rather than as a quotable passage.
- The one-time legacy migration is applied as part of upgrading to this feature without an extra command from the reader, and is driven only by the documented legacy marker rule.
- Records received after the migration are never reinterpreted by the legacy rule.
- Notes remain visible in the product's existing surfaces - the shared annotation list, the per-book view and recaps - because those are where the reader already reads their highlights, and the same information is exposed to the web portal and to the CLI.
- Editing or deleting notes, markers and tags inside the product, acting on tag names, delivery formats, export formats, mastery cards, sync scheduling and AI-assisted classification are out of scope; this feature guarantees the data those features need exists and is exposed.
- The reader's own highlights and notes are the only source of chapter information; no external table of contents is fetched, and headings stored as images in a book cannot be captured.
