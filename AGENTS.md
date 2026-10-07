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
- Web UI work (`src/relego.web`, `src/landing`): use the `impeccable` skill before changing UI

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

This repository uses GitHub Spec Kit with the Copilot skills integration. Run every spec-kit operation through the `specify` CLI and never hand-edit `.specify/` internals or generated artifacts. Spec Kit skills live under `.github/skills/speckit-<command>/SKILL.md` and run inside the CLI workflow.

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

PRDs in `docs/prds/` define milestones and contain no user stories. Once a PRD is merged to `main`, create its features as spec packages under `specs/` by running the workflow from the repository root:

```sh
specify workflow run speckit -i spec="Describe the feature to build"
```

The workflow pauses after specification and planning for review. Inspect and resume runs with:

```sh
specify workflow status
specify workflow status <run-id>
specify workflow resume <run-id>
```

**Feature flow**

1. Review at each checkpoint and capture every user decision instead of letting the tool guess scope or behavior.
2. After `spec.md` is ready, create exactly one GitHub issue for the spec and add it to the kanban as-is. Feature issues carry the `enhancement` label.

```sh
gh issue create --title "<feature name>" --label enhancement --body "Spec: https://github.com/Krusty93/relego/blob/main/specs/00X-name/spec.md"
gh project item-add 2 --owner krusty93 --url <issue-url>
```

3. Merge the spec package to `main` before implementation starts.
4. Implement one task per PR. `/speckit-implement` runs as designed; the stacked-PR workflow below governs delivery.

The spec package under `specs/00X/` is the single source of truth for the feature. Issues and PRs link to it and never restate requirements. Do not add ad-hoc files under `specs/` or run spec-kit for work that is not tied to a tracked feature.

## ADR conventions

ADRs live in `docs/adr/`. Statuses: `accepted` · `active` (under decision) · `retired` · `superseded`.
When superseded, both involved ADRs must link to each other.
During spec generation, if a significant architectural decision is made, ask the user whether to record it as an ADR. If they do not answer, ask again at the next opportunity. On approval, write the ADR with a subagent in `docs/adr/`.

## GitHub Project conventions

**Kanban:** project #2 on `Krusty93/relego`. Add every new issue immediately after creation:

```sh
gh project item-add 2 --owner Krusty93 --url <issue-url>
```

### PR ↔ issue rules

- Spec PRs reference the spec's issue with `Relates to #N` (one PR per task; see the workflow below).
- PRs outside a spec (dependency updates, docs, chores, devcontainers) reference a tracking issue with `Closes #N`; ask the user whether to create that issue first, then add it to the kanban.
- PRs use `.github/pull_request_template.md`.
- Feature work carries the `enhancement` label; `bug`, `documentation`, and `good first issue` cover the rest.

### tasks.md rules

- Mark a task `[X]` on the same branch where the work was done, before pushing
- Never leave `[ ]` on a branch where that task's work is already committed

## PR workflow (per task, stacked)

Stacking uses the `gh-stack` extension. Layers follow the dependency and execution order in `tasks.md`; each PR is based on the branch below it, and each PR marks its task `[X]`.

1. `git checkout main && git pull`
2. Start the stack for the first task: `gh stack init task/TXXX-short-description`
3. Implement the task; mark it `[X]` in `tasks.md`; commit both together. Setup and Foundational tasks belong to the first PR; Polish tasks belong to the last.
4. Layer the next task: `gh stack add task/TXXX-short-description`, then repeat step 3
5. Open the chained PRs with `gh stack submit`; fill every body from `.github/pull_request_template.md` and reference the spec issue with `Relates to #N`
6. After review changes: `gh stack rebase` then `gh stack push`. After merges: `gh stack sync` (or `gh stack merge` to merge a run)
7. If applicable, update living docs (`ARCHITECTURE.md`, etc.) in the same PR

Parallel `[P]` tasks may run concurrently with parallel agents or separate git worktrees; their branches sit side by side on the same base instead of stacking on each other.

## Versioning conventions

Refer to the canonical versioning guide in [VERSIONING.md](../VERSIONING.md).

## About commits

Do NOT use conventional commits, nor in PR.

## Maintaining this file

Keep `AGENTS.md` within 200 lines. Move package/subdomain-specific detail into a nested `AGENTS.md` in that folder (e.g. `src/relego.web/AGENTS.md`) instead of growing this file; nested files apply to their subtree.
