# Relego Constitution

## Core Principles

### I. Client/Server Separation
The system is split into two independently deployable components: the `relego` CLI (client) and `relego-server` (server). The server owns all automated operations (scheduling, recap generation, and email delivery) and hosts the REST API and web UI. The client owns on-device import and interactive commands. Neither component shall take over the responsibilities of the other.

### II. Zero-Config Onboarding
The user must be up and running in under 2 minutes from a cold start. No email address is required to use Relego: import, storage, and browsing work without any delivery setup. A delivery destination (Send-to-Kindle address or inbox email) is optional and needed only for recap delivery. All other settings have sensible defaults.

### III. Local Processing Only
No data is ever sent to third-party services other than the mail channel the user configures. All highlight processing, spaced repetition selection, and recap composition happen on the user's own infrastructure.

### IV. Tests Ship with the Code
Every PR that introduces or changes behavior must include the corresponding tests in the same PR. TDD is encouraged where it helps (parsers, REST endpoints, and other behavior-heavy work) and is not required for purely mechanical changes. Unit tests cover domain logic; integration tests cover REST endpoints and email delivery.

### V. Simplicity Over Premature Generalization
Relego is single-user and unauthenticated by design. Do not add multi-user support, authentication, or performance optimizations until explicitly required. YAGNI.

## Technology Constraints

- **Language:** C# / .NET 10 — no mixing of languages or runtimes
- **Storage:** SQLite at `/data/relego.db` — no second database container
- **Logging:** Serilog with file and SQLite sinks — no raw `Console.WriteLine` for diagnostic output
- **Email:** MailKit with user-provided SMTP — no third-party email SaaS
- **Scheduling:** Quartz.NET — no custom scheduler implementations
- **CLI UX:** Spectre.Console — no raw `Console.WriteLine` for user-facing output
- **Protocol:** REST HTTP + JSON — no gRPC, no GraphQL
- **Server distribution:** Docker only — no native packages for the server

## Governance

This constitution is long-lived and is not bound to any PRD, milestone, or release. It is amended only when the product's requirements or engineering standards change; product scope lives in `docs/prds/` and `specs/`, and system design lives in `docs/ARCHITECTURE.md`, `docs/DX.md` and `docs/BRAND_COLORS.md`.

This constitution supersedes other implementation guidelines. Any deviation requires explicit documentation in an ADR, and every PR review verifies compliance with these principles. Amendments require explicit approval before implementation and follow semantic versioning (MAJOR.MINOR.PATCH).

**Version**: 1.1.1 | **Ratified**: 2026-04-02 | **Last Amended**: 2026-10-05

