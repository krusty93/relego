# Architecture — Relego

**Version:** 0.2 — Draft
**Date:** 2026-06-02
**Status:** Draft

---

## System Overview

Relego follows a client/server architecture with two independently deployable components.

```
User Laptop                              Home Server / NAS / Pi
-----------                              ----------------------
relego CLI  ── REST HTTP ──────────────▶  relego-server (Docker)
    │                                       ├── Scheduler (Quartz.NET)
    │ USB                                   ├── SMTP sender (MailKit)
    ▼                                       └── SQLite (Docker volume)
Kindle / My Clippings.txt                              │
                                                       │ SMTP
                                                       ▼
                                            Amazon Send-to-Kindle
                                                       │
                                                       ▼
                                                  Kindle (e-ink)
```

---

## Components

### Client CLI (`relego`)

- Distributed as a self-contained binary (macOS/Linux/Windows) or runnable via Docker
- Optional Docker image for no-install usage: `ghcr.io/krusty93/relego.cli`
- Reads the server URL from client configuration (`Server:Url`) with runtime override via `SERVER_URL` (no authentication — local network trusted)
- Responsibilities:
  - Parse and sync highlights from `My Clippings.txt` to the server
  - Manage user settings via CLI commands (schedule, count, weights, exclusions)
  - Display server status

Responsible for transforming raw Kindle export text into structured data before syncing to the server.

- **Entry point**: `ClippingsParser.ParseAsync(string filePath, ILogger? logger = null)` — file-path overload; `ClippingsParser.ParseAsync(TextReader, ILogger? logger = null)` — streaming overload for testability
- **Output types**:
  - `ParseResult` — top-level result: list of `ParsedBook`, total entries processed, duplicates removed
  - `ParsedBook` — `(Title, Author?, IReadOnlyList<ParsedHighlight> Highlights)`
  - `ParsedHighlight` — `(Text, Location?, AddedOn?)`
- **Design decisions**:
  - Streaming: reads lines one-by-one via `ReadLineAsync()`; no full file in memory
  - Skip-and-warn: malformed entries are skipped with an `ILogger.LogWarning`; never throws
  - Deduplication: `HashSet<(Title, Author, Text)>` — exact case-sensitive match, first occurrence kept
  - Notes as highlights: entries of type "Note" are emitted as highlights with `[my note] ` prefix on their text
  - Bookmarks: entries of type "Bookmark" are silently dropped

#### TUI subsystem (`Relego.Cli/Tui/`)

When invoked with no arguments in an interactive terminal (`relego`), the client enters **TUI mode** — a full-screen terminal UI powered by [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) (v2).

**Dual-mode launch** (`Program.cs`): if `args.Length == 0 && !Console.IsInputRedirected`, `TuiApp.RunAsync()` is called; otherwise the Spectre.Console `CommandApp` handles the sub-command (e.g. `relego sync`).

### Server (`relego-server`)

- Distributed as a Docker container
- Published to GHCR as `ghcr.io/krusty93/relego.server`
- Always-on, handles all automated operations
- Responsibilities:
  - Store highlights, recap history, weights, exclusions, settings in SQLite
  - Run scheduled recap generation (daily or weekly, configurable time)
  - Select highlights via spaced repetition algorithm
  - Compose recap document and send via SMTP to Kindle email address
  - Expose REST HTTP API consumed by the client CLI

#### REST API layer (`Relego.Server/`)

The server currently exposes the MVP storage API as ASP.NET Minimal APIs.

- Composition root: `Program.cs`
- Endpoint modules: `Endpoints/`
- Data access: `Data/`
- Shared request/response contracts: `Relego.Core/Contracts/`
- OpenAPI: Swagger UI is enabled only in Development

The application registers a scoped `IDbConnection` backed by `Microsoft.Data.Sqlite`, opens the connection per request, enables SQLite foreign keys via `PRAGMA foreign_keys = ON`, and resolves thin repository classes over that connection.

Endpoint groups currently implemented:

- Sync: bulk import via `POST /highlights/import`
- Settings: `GET /settings`, `PATCH /settings`, `POST /settings/test-kindle-email`, `POST /settings/test-recap-email`
- Status: `GET /status`
- Recap: `POST /recaps`
- Highlights: `GET /highlights`, `DELETE /highlights/{id}`
- Books: `PUT /books/{id}/title`
- Exclusions: `*/{id}/exclusions` plus `GET /exclusions`
- Weights: `PUT /highlights/{id}/weight`, `GET /highlights/weights`

---

### Landing Page (`src/landing/`)

Static marketing landing page built with Astro and Tailwind CSS. Completely independent from the .NET solution: separate `package.json`, separate build, separate test suite.

- **Tech stack**: Astro 6, Tailwind CSS v4 (via PostCSS), Playwright for E2E testing
- **Deployment target**: GitHub Pages (static HTML output, no server required)
- **Build**: `cd src/landing && npm run build` → outputs to `src/landing/dist/`
- **Tests**: `cd src/landing && npx playwright test` — Chromium-only, includes axe-core accessibility audit
- **Separation**: no shared code, no shared dependencies with the .NET projects; the landing page can be deployed and developed independently

---

## Technology Stack

| Component                | Technology                     | Rationale                                               |
|--------------------------|--------------------------------|---------------------------------------------------------|
| Language / runtime       | .NET 10 (C#)                   | Cross-platform, self-contained binaries, rich ecosystem |
| Client distribution      | Single-file binary / Docker    | Zero runtime dependency for end users                   |
| Server distribution      | Docker container               | Self-hosted, single command to deploy                   |
| Storage                  | SQLite (file in Docker volume) | Zero config, single file, no extra container            |
| Client/server protocol   | REST HTTP                      | Simple, debuggable, universally supported               |
| Email delivery           | MailKit + SMTP                 | Industry standard, supports Send-to-Kindle              |
| Logging                  | Serilog (file + SQLite sink)   | Structured logging, persistent, queryable               |
| Scheduling               | Quartz.NET                     | Mature .NET scheduler, cron-style expressions           |
| CLI UX                   | Spectre.Console                | Rich terminal output, tables, progress bars             |
| Landing page             | Astro + Tailwind CSS           | Static site generation, minimal JS, fast build          |

---

## Data Model

```
users            (id, kindle_email[''], delivery_email[NULL], created_at)
authors          (id, name)
books            (id, user_id, author_id, title)
highlights       (id, user_id, book_id, text, weight[1-5], excluded, last_seen, delivery_count, created_at)
excluded_books   (id, user_id, book_id, excluded_at)
excluded_authors (id, user_id, author_id, excluded_at)
settings         (user_id, schedule['daily'|'weekly'], delivery_day, delivery_time[default:'18:00'], count[1-15, default:3])
```

> **Delivery destinations:** both `kindle_email` and `delivery_email` are optional; at least one must be set for recap delivery. `POST /recaps` returns HTTP 422 when neither is configured; the TUI shows a persistent warning.

> **MVP note:** Single-user only. The server auto-creates or reuses user `id = 1` on demand for every API request.

Current uniqueness constraints used by the REST layer:

- `authors(name)`
- `books(user_id, author_id, title)`
- `highlights(user_id, book_id, text)`

---

## Core Query — Recap Selection

```sql
SELECT h.*
FROM highlights h
WHERE h.user_id = @userId
  AND h.excluded = 0
  AND h.book_id NOT IN (SELECT book_id FROM excluded_books WHERE user_id = @userId)
  AND h.author_id NOT IN (SELECT author_id FROM excluded_authors WHERE user_id = @userId)
ORDER BY (h.weight * RANDOM()) DESC, h.last_seen ASC
LIMIT @count
```

---

## REST API Surface

| Method   | Path                              | Description                                 | Tag        |
|----------|-----------------------------------|---------------------------------------------|------------|
| `POST`   | `/highlights/import`              | Bulk import highlights from client          | Sync       |
| `GET`    | `/status`                         | Server status, next recap, highlight stats  | Status     |
| `GET`    | `/settings`                       | Read current settings                       | Settings   |
| `PATCH`  | `/settings`                       | Partially update settings                   | Settings   |
| `POST`   | `/settings/test-kindle-email`     | Send a test email via Send-to-Kindle        | Settings   |
| `POST`   | `/settings/test-recap-email`      | Send a test HTML email to the inbox address | Settings   |
| `POST`   | `/recaps`                         | Execute a recap immediately                 | Recap      |
| `GET`    | `/highlights`                     | List/paginate/search highlights             | Highlights |
| `DELETE` | `/highlights/{id}`                | Delete a highlight                          | Highlights |
| `PUT`    | `/highlights/{id}/weight`         | Set highlight recap weight                  | Weights    |
| `GET`    | `/highlights/weights`             | List weighted highlights                    | Weights    |
| `PUT`    | `/books/{id}/title`               | Rename a book                               | Books      |
| `POST`   | `/highlights/{id}/exclusions`     | Exclude a highlight                         | Exclusions |
| `DELETE` | `/highlights/{id}/exclusions`     | Re-include a highlight                      | Exclusions |
| `POST`   | `/books/{id}/exclusions`          | Exclude a book                              | Exclusions |
| `DELETE` | `/books/{id}/exclusions`          | Re-include a book                           | Exclusions |
| `POST`   | `/authors/{id}/exclusions`        | Exclude an author                           | Exclusions |
| `DELETE` | `/authors/{id}/exclusions`        | Re-include an author                        | Exclusions |
| `GET`    | `/exclusions`                     | List all exclusions                         | Exclusions |

### Data access pattern

The REST layer uses Dapper with explicit SQL rather than EF Core.

- Each repository encapsulates one domain slice and receives `IDbConnection` via DI
- Queries stay close to the endpoint behavior they support
- Sync import uses a database transaction to keep author, book, and highlight insertion consistent
- Read models returned by list endpoints are projected directly into DTOs rather than materializing richer domain aggregates

Current repository split:

- `UserRepository`: implicit MVP user bootstrap and user email persistence
- `SyncRepository`: bulk import and deduplication
- `SettingsRepository`: settings read/upsert
- `StatusRepository`: aggregate counters
- `ExclusionRepository`: inclusion/exclusion mutations and exclusion listings
- `WeightRepository`: weight updates and weighted highlight listings

### Error handling

The API returns JSON-only responses.

- Validation failures use `Results.ValidationProblem(...)` and return HTTP `422`
- Missing entities use `Results.Problem(...)` and return HTTP `404`
- Successful mutations that do not need a body return HTTP `204`
- Successful reads return HTTP `200` with DTO payloads from `Relego.Core/Contracts/`

This keeps the client protocol small, explicit, and aligned with the quickstart `curl` flows.

### Contract naming conventions

Transport objects in `Relego.Core/Contracts/` follow these suffixes:

| Suffix      | Usage                                                                                                          |
|-------------|----------------------------------------------------------------------------------------------------------------|
| `*Request`  | Inbound root-level request bodies (e.g. `SyncRequest`, `UpdateSettingsRequest`)                                |
| `*Response` | Outbound root-level response bodies (e.g. `StatusResponse`, `HighlightsResponse`)                              |
| `*Dto`      | Nested data-transfer objects used as list items or sub-objects within a response (e.g. `WeightedHighlightDto`) |

## Project structure

```tree
src/Relego.Core/
└── Contracts/          # Shared request/response DTOs for CLI and server

src/Relego.Cli/
├── Commands/           # Spectre.Console CLI sub-commands (sync, status, config, …)
├── Infrastructure/     # HTTP client, resilience, Kindle detector
├── Parsing/            # My Clippings.txt parser
├── Tui/                # Terminal.Gui TUI (TuiApp, screens, StatusChrome, …)
└── Program.cs          # Dual-mode entry point (TUI or CLI)

src/Relego.Server/
├── Data/               # Dapper repositories over SQLite
├── Endpoints/          # Minimal API endpoint modules
├── Infrastructure/     # Database bootstrap and logging
├── Models/             # Server-side domain models
└── Program.cs          # Composition root and DI wiring

src/Relego.Tests/
├── Api/                # End-to-end HTTP integration tests via WebApplicationFactory
├── Cli/                # CLI command tests
├── Infrastructure/     # Database/bootstrap tests
├── Parsing/            # CLI parser tests
├── Recap/              # Recap service tests
└── Tui/                # TUI logic tests (mode detection, search, screen key handling)

src/landing/                # Static marketing landing page (independent from .NET)
├── pages/              # Astro pages (index.astro)
├── components/         # Reusable Astro components (Navbar, Footer, Section, etc.)
├── config/             # Site configuration (site.ts)
├── layouts/            # Layout wrapper
├── styles/             # Global CSS with Tailwind + CSS variables
├── assets/             # Images (hero)
└── tests/              # Playwright E2E tests (navigation, theme, content, a11y)
```

---

## ADR Index

| ADR                                                     | Decision                                     |
|---------------------------------------------------------|----------------------------------------------|
| [ADR-001](adr/001-client-server-architecture.md)        | Client/server architecture                   |
| [ADR-002](adr/002-dotnet-core-runtime.md)               | .NET Core as language/runtime                |
| [ADR-003](adr/003-sqlite-storage.md)                    | SQLite as storage engine                     |
| [ADR-004](adr/004-rest-http-protocol.md)                | REST HTTP as client/server protocol          |
| [ADR-005](adr/005-my-clippings-txt-highlight-source.md) | `My Clippings.txt` as MVP highlight source   |
| [ADR-006](adr/006-docker-only-distribution.md)          | Docker-only server distribution              |
| [ADR-007](adr/007-dual-channel-email-delivery.md)       | Dual-channel email delivery                  |
| [ADR-008](adr/008-kobo-reader-sqlite-source.md)         | `KoboReader.sqlite` as Kobo highlight source |
