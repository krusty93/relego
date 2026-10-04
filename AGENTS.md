# Relego

> Quick links: [Architecture](./docs/ARCHITECTURE.md) · [DX](./docs/DX.md) · [PRDs](./docs/prds/)

## Project overview

Self-hosted tool that delivers Kindle highlight recaps to the user's Kindle via Send-to-Kindle email. Architecture: `relego` CLI (client) + `relego-server` Docker container (server).

**Stack:** C# / .NET 10 · SQLite (`/data/relego.db`) · Serilog · MailKit · Quartz.NET · Spectre.Console · REST HTTP (no auth, MVP)

**Solution:** `src/Relego.slnx` → Core · Server · Cli · Tests. Web UI in `src/relego.web` (React/Vite); landing page in `src/landing` (Astro)

## Build and test commands

Prerequisites: .NET 10 SDK and Docker. Node.js/npm only when touching `src/relego.web` or `src/landing`. Full setup details in [CONTRIBUTING.md](./CONTRIBUTING.md).

**Build:**

- Whole solution: `dotnet build src/Relego.slnx`; single components: `dotnet build src/Relego.Server/Relego.Server.csproj`, `dotnet build src/Relego.Cli/Relego.Cli.csproj`
- Landing page: `cd src/landing && npm install && npm run build`
- Docker: `docker compose up --build`; the `ci.yaml` Build step builds the `Dockerfile.ci` image only

**Test:**

- .NET: `dotnet test src/Relego.Tests/Relego.Tests.csproj`
- Web UI (Playwright): `cd src/relego.web && npm ci && npx playwright install --with-deps chromium && npm test` (typecheck: `npm run typecheck`)
- CI: the `ci.yaml` Run tests step runs `dotnet test --configuration Release --no-build` inside the CI image; a separate job runs the Playwright web tests

## Code style guidelines

- Follow existing .NET and C# conventions; formatting and analyzers are enforced at build time via `.editorconfig` and `src/Directory.Build.props` (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel=latest`, XML docs on public members; projects enable `Nullable` and `ImplicitUsings`).
- Naming: `_camelCase` private fields, PascalCase public/static/readonly fields and constants, camelCase locals and parameters, `Async` suffix on async methods.
- 4-space indent and UTF-8 BOM in C# files; `System.*` usings first; no `this.`; no multiple blank lines; async all the way (no blocking calls).
- All REST endpoints return JSON; errors must be actionable.
- When adding new .NET projects: `dotnet sln src/Relego.slnx add src/<Project>/<Project>.csproj` in the same PR
- Diagrams: Mermaid preferred; ASCII only for spatial layouts

## Testing instructions

- xUnit tests live in `src/Relego.Tests/`, mirroring source folders (`Api/`, `Cli/`, `Parsing/`, `Sources/`, `Recap/`, `Services/`, `Infrastructure/`).
- API tests use `Microsoft.AspNetCore.Mvc.Testing` via `RelegoTestApplicationFactory`; HTTP is mocked with `RichardSzalay.MockHttp`; fixtures live in `src/Relego.Tests/Fixtures/`, and SQLite sources use the `KoboTestDatabase` helper.
- Use TDD where applicable, especially for API endpoints, parsers, and other behavior-heavy changes. Tests are not required for purely mechanical changes such as NuGet updates or `.csproj` edits.
- New highlight sources: add focused tests under `src/Relego.Tests/Sources/` plus a fixture; see [CONTRIBUTING.md](./CONTRIBUTING.md) for the full checklist.

## Security considerations

- Assume a trusted local network; never expose the server publicly without a reverse proxy and auth (ADR-004).
- Never log or commit secrets; SMTP credentials and delivery addresses seed from env vars on first boot and are stored server-side in SQLite.
- SQLite data lives at `/data/relego.db` (Docker volume) — back it up; Kobo databases are copied to a temp file, opened read-only, and deleted so device files are never modified (ADR-008).
- Report vulnerabilities privately via GitHub Security Advisories (see [SECURITY.md](./SECURITY.md)), never in public issues.
- Supply chain: NuGet audit with a documented `GHSA-2m69-gcr7-jv3q` suppression, OSSF Scorecard and CodeQL in CI, GitHub Actions pinned by SHA.

## Spec Kit integration

This repository uses GitHub Spec Kit with the Copilot skills integration. Spec Kit skills live under `.github/skills/speckit-<command>/SKILL.md` and are invoked with `/speckit-<command>`.

Core commands:

- `/speckit-constitution`
- `/speckit-specify`
- `/speckit-plan`
- `/speckit-tasks`
- `/speckit-implement`

Optional quality commands:

- `/speckit-clarify`
- `/speckit-checklist`
- `/speckit-analyze`
- `/speckit-converge`

The complete tracked-feature workflow can be run from the repository root:

```sh
specify workflow run speckit -i spec="Describe the feature to build"
```

The workflow pauses after specification and planning for review. The Relego-specific `relego-review` overlay also pauses before implementation. Approve that gate only after the design package is approved, the design PR is merged, and GitHub Project implementation phase subtasks have been created from `tasks.md`. Inspect and resume runs with:

```sh
specify workflow status
specify workflow status <run-id>
specify workflow resume <run-id>
```

Do not run `/speckit-implement` until the feature issue and implementation subtask exist, the design package has been reviewed and approved, the design PR has been merged, implementation subtasks are ready, and the relevant issue has moved to `In progress`.

## ADR conventions

ADRs live in `docs/adr/`. Statuses: `accepted` · `active` (under decision) · `retired` · `superseded`.
When superseded, both involved ADRs must link to each other.
Register a new ADR whenever a significant architectural decision is made during spec-kit design.

## GitHub Project conventions

**Kanban:** project #2 on `Krusty93/relego`. Use `gh` CLI to resolve IDs at runtime:
- Project ID + status field ID: `gh project view 2 --owner Krusty93 --format json`
- Status option IDs: `gh project field-list 2 --owner Krusty93 --format json`
- Item ID for an issue: `gh api graphql -f query='{ repository(owner:"Krusty93", name:"relego") { issue(number:N) { projectItems(first:1) { nodes { id } } } } }'`
- Move item: `gh project item-edit --id <ITEM_ID> --project-id <PROJECT_ID> --field-id <FIELD_ID> --single-select-option-id <OPTION_ID>`

Status names: `Backlog` · `Ready` · `In progress` · `In review` · `Done`

### Task lifecycle

**Before starting any task or feature:** move the kanban item to `In progress`, then begin implementation.
**On PR open:** move to `In review`. **On PR merge:** move to `Done`.

### Task structure for spec-kit features

Each feature (e.g. `003-highlight-parser`) has **one parent task** on the kanban with label `feature:00X-name`. The parent task contains:

1. **Design subtask** — runs Spec Kit (`/speckit-specify` → `/speckit-plan` → `/speckit-tasks`); produces `specs/00X/spec.md`, `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, `tasks.md`
2. **Implementation subtasks** — one per phase defined in `tasks.md`; each subtask carries the same label as the parent

### Feature start sequence

When asked to start a feature, follow this exact order **before writing any code or spec**:

1. Create the **Design subtask** issue (label = parent label), add to kanban → move to `In progress`
2. Create the **Implementation subtask** issue (label = parent label), add to kanban → leave in `Backlog`
3. Run Spec Kit: `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` — **each step requires user involvement**: ask the user for all decisions, feature preferences, constraints, and clarifications before proceeding to the next step; do not make assumptions on scope or design choices
4. Open the design PR and move the Design subtask to `In review`.
5. **After the design PR merges**, move the Design subtask to `Done`, then create **one implementation phase subtask** per phase defined in `tasks.md`. Each phase subtask must be a child of the Implementation subtask, use the same feature label, and be added to the GitHub Project Kanban in `Backlog`.
6. Move the Implementation subtask to `In progress`, then begin phase-by-phase implementation.

For non-feature tasks (e.g. CI/CD pipeline), check existing labels first. If no label matches, ask the user before proceeding.

Task descriptions must be self-contained: an agent must be able to implement a task by reading only the repo docs and the task description.

### PR ↔ Task link rules

- Every PR must close a GitHub issue via `Closes #N` in the body
- PR labels must match the linked task's label
- Opening a PR → move task to `In review`
- Merging a PR → move task to `Done`
- If no issue exists, create one first, add it to the kanban project, then open the PR

### tasks.md rules

- Mark a task `[X]` on the same branch where the work was done, before pushing
- Never leave `[ ]` on a branch where that task's work is already committed

## Implementation workflow (per PR)

1. `git checkout main && git pull && git checkout -b task/TXXX-short-description`
2. Implement; mark `[X]` in `tasks.md`; commit both together
3. If applicable, update living docs (`ARCHITECTURE.md`, etc.) in the same PR
4. `gh pr create --title "<descriptive title, no conventional commit prefix>" --body "... Closes #N" --label "..." --base main`
5. Move kanban → `In review`
6. After merge: `git pull main`, move kanban → `Done`

## Versioning conventions

Refer to the canonical versioning guide in [VERSIONING.md](../VERSIONING.md).

## About commits

Do NOT use conventional commits, nor in PR.

## Maintaining this file

Keep `AGENTS.md` within 200 lines. Move package/subdomain-specific detail into a nested `AGENTS.md` in that folder (e.g. `src/relego.web/AGENTS.md`) instead of growing this file; nested files apply to their subtree.
