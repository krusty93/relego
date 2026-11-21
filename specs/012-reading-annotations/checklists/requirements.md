# Specification Quality Checklist: Reading Annotations, Notes, Chapters, and Tags

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-04-18
**Updated**: 2026-04-18 (fidelity pass)
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Fidelity review

- [x] Heading markers recognised as a prefix, so a marker followed by a summary on a new line yields a chapter with a section note
- [x] Section notes rendered beneath the heading
- [x] Deeper-level headings rendered as headings, while the table of contents requires at least one level-1 marker
- [x] Each annotation shows chapter context in place of a bare position number, with the raw position retained
- [x] Table of contents both navigates to a chapter and filters the annotation list
- [x] Stitching markers join fragments in reading order, joined by an ellipsis, with a fresh passage at every first-level marker and no stitching across books
- [x] Stitched fragments stay individually stored with their own position, note, tags and curation state, so removing a marker never deletes data
- [x] Leading single-word dot tokens become tags, tags can coexist with a note in the same field, and repeated tags apply once
- [x] Tags are filterable from both the book view and the whole list, matched without regard to letter case
- [x] Reserved tokens recognised by shape rather than by a fixed list, so unknown tags stay visible instead of being hidden or reinterpreted
- [x] Annotation identity independent of note and markers, so markers can be added after import without duplicating records
- [x] Marker level changes re-parent descendants; losing a marker never deletes annotations
- [x] Empty or whitespace-only notes treated as no note; multi-line notes preserved exactly
- [x] Web portal and CLI required to report the same note, chapter context, stitched grouping and tags

## Notes

- Scope decisions recorded during specification: notes are modelled as one annotation carrying an optional note (no separate note records); the legacy `[my note] ` records are migrated once by rule; chapter navigation includes a table of contents plus chapter filtering; stitched passages and inline tags are derived from the same reserved-token rules as chapter markers.
- Deliberate bounded choices (see Assumptions): stitched passages and tags are derived views over fragment records rather than destructive merges; tag names carry no automatic behaviour in this feature; chapter structure is recomputed; migration is applied automatically on upgrade; headings carried as images in a book cannot be captured.
- Deferred to later features (see Assumptions): editing notes or tags in the product, tag-driven behaviour (excluding, weighting, prioritising), delivery and export formats, mastery cards, sync scheduling, AI-assisted classification.
- Documentation is deliberately not a user story: it is a Polish-phase task in `tasks.md`, matching the repo's existing specs. The pages that must change are the reading-side docs under `src/landing/content/docs/docs/` (`import.md` currently claims only quote, title and author are read) plus `reference/cli.md` if the CLI gains annotation listing.
- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`
