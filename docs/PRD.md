# Product Requirements Document — Relego

**Version:** 0.2 — Draft
**Date:** 2026-03-30
**Status:** Draft

---

## Overview

Relego is a self-hosted, open source recap system for ebook highlights. The CLI imports highlights from registered local sources — Kindle `My Clippings.txt` and Kobo `.kobo/KoboReader.sqlite` today — and syncs them to a self-hosted server. The server selects periodic recap highlights with spaced repetition and sends them through the configured delivery destination: Amazon Send-to-Kindle for Kindle users and/or regular inbox email for HTML recaps.

The highlight-source layer is intentionally extensible. New sources plug in through the open `IHighlightSource` registry described in [ARCHITECTURE.md](ARCHITECTURE.md) and [ADR-008](adr/008-kobo-reader-sqlite-source.md), with contributor guidance in [CONTRIBUTING.md](../CONTRIBUTING.md#adding-a-new-highlight-source).

---

## Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-01 | Import highlights and annotations from registered highlight sources, including Kindle `My Clippings.txt` and Kobo `.kobo/KoboReader.sqlite` | Must |
| FR-02 | Deduplicate highlights across imports and sources | Must |
| FR-03 | Group highlights by book title and author | Must |
| FR-04 | Compose a recap document from the parsed highlights | Must |
| FR-05 | Send recaps to configured delivery destinations: Kindle email and/or regular inbox email | Must |
| FR-06 | Support configurable recap schedule (daily or weekly) | Must |
| FR-07 | Provide sensible defaults for all settings except the delivery destination, requiring minimal configuration to get started | Must |
| FR-08 | Track recap history per highlight (delivery count, last seen date) | Must |
| FR-09 | Select highlights for each recap using spaced repetition — highlights seen less recently are prioritized | Must |
| FR-10 | Produce recap output compatible with Kindle's native document rendering and regular email inbox reading | Must |
| FR-11 | Allow the user to assign a weight to each highlight (higher weight = higher probability of appearing in recaps) | Must |
| FR-12 | Allow the user to exclude specific highlights, books, or authors from all future recaps, and to re-include them at any time | Must |
| FR-13 | Expose all settings management via CLI commands — settings are stored server-side, not in a local file edited by the user | Must |
| FR-14 | Allow the user to configure the number of highlights per recap (min 1, max 15, default 3) | Must |
| FR-15 | Allow additional highlight sources to be added through `IHighlightSource` registration without resolver, workflow, command, or enum edits | Should |

---

## Non-Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| NFR-01 | Distributed and run exclusively via Docker | Must |
| NFR-02 | No external runtime dependencies beyond a container or a standard language runtime | Should |
| NFR-03 | All user-facing configuration is managed via CLI — no manual file editing required | Must |
| NFR-04 | Licensed under MIT or Apache 2.0 | Must |
| NFR-05 | No data sent to third-party services — all processing is local | Must |
| NFR-06 | Recap generation completes in under 30 seconds for a 10,000-highlight file | Should |
| NFR-07 | Recap document renders correctly on Kindle Paperwhite (any generation) | Must |
| NFR-08 | Import from a readable source without modifying the source file or device database | Must |

---

## User Stories

| ID | Story | Priority |
|---|---|---|
| US-01 | As a reader, I want to connect my Kindle or Kobo via USB and point the tool at the device or source file, so that my highlights are available for recap generation | Must |
| US-02 | As a user, I want to provide only my delivery address to get started, with all other settings applied automatically as defaults, so that onboarding requires minimal effort | Must |
| US-03 | As a user, I want to choose between daily and weekly recap delivery via CLI, so that I can adjust the frequency without editing any file manually | Must |
| US-04 | As a user, I want each recap to surface highlights I haven't seen recently, weighted by my preferences, so that repeated exposure helps me retain what matters most to me | Must |
| US-05 | As a user, I want the recap to open on my configured reading surface, so that I can revisit highlights comfortably | Must |
| US-06 | As a user, I want to run the tool with a single command after initial setup, so that day-to-day usage requires no technical knowledge | Must |
| US-07 | As a self-hoster, I want to run the server component as an always-on Docker container on my home server or NAS, so that recaps are sent automatically without requiring my laptop to be on | Must |
| US-08 | As a user, I want clear error messages when email delivery fails, so that I can diagnose and fix configuration issues quickly | Should |
| US-09 | As a user, I want to mark a highlight/book/author as excluded via CLI, so that it never appears in my recaps | Must |
| US-10 | As a user, I want to assign a higher weight to specific highlights via CLI, so that they appear more frequently than others | Must |
| US-11 | As a Kobo user, I want Relego to import highlights from `KoboReader.sqlite` without modifying the device database, so that I can use the same recap workflow safely | Must |
| US-12 | As a contributor, I want a documented highlight-source extension model, so that I can add a new source without changing shared resolver, workflow, command, or enum code | Should |

---

## MoSCoW Prioritization

### Must Have (MVP)

- Import from supported highlight sources, deduplicate, and group by book (FR-01, FR-02, FR-03)
- Compose and send recap output to configured Kindle or inbox email destinations (FR-04, FR-05, FR-10)
- Track recap history per highlight and apply spaced repetition for selection (FR-08, FR-09)
- User-defined highlight weights and exclusions (FR-11, FR-12)
- Configurable schedule: daily or weekly (FR-06)
- Zero-config onboarding with sensible defaults (FR-07)
- CLI-based settings management, stored server-side (FR-13)
- Configurable highlights per recap: min 1, max 15, default 3 (FR-14)
- Docker-only distribution (NFR-01)
- All processing local, no third-party data sharing (NFR-05)
- MIT or Apache 2.0 license (NFR-04)

### Should Have

- Clear error messages for SMTP/delivery failures (US-08)
- Documented source registry for adding future highlight sources without shared-code branching (FR-15, US-12)
- Sub-30s performance for large clippings files (NFR-06)
- Minimal runtime dependencies (NFR-02)

### Could Have (post-MVP)

- Readwise integration as optional connector
- Web clipper integration
- Additional highlight sources (e.g. Apple Books, Readwise exports, web exports)
- Kobo on-device delivery through Dropbox or Google Drive folder sync
- Recap format customization (font size, density, layout)

### Won't Have (explicitly out of scope)

- Mobile app
- Web UI
- Cloud SaaS version
- AI summarization of highlights
- Social or sharing features
- Scraping `read.amazon.com`

---

## MVP Definition

The MVP is complete when a user can:

1. Deploy the server component as a Docker container on a home server, NAS, or Raspberry Pi
2. Run the client CLI on their laptop, point it at a supported highlight source, and provide at least one delivery destination
3. Receive a correctly formatted recap on the configured schedule, with highlights selected via spaced repetition
