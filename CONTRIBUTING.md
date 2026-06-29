# Contributing to Relego

Thank you for your interest in contributing. Relego uses an issue-first workflow: work starts from a GitHub issue, moves through the project board, and lands in a pull request that closes that issue.

If you are changing user-facing behavior, review [README.md](README.md) and [docs/DX.md](docs/DX.md) first so the implementation, wording, and examples stay aligned.

## Minimum first contribution path

For a first contribution, prefer a small issue labeled `documentation` or `good first issue`.

1. Pick an existing issue, or open one with the appropriate template.
2. Comment on the issue so work is not duplicated.
3. Create a branch from `main`.
4. Make one focused change.
5. Run the smallest relevant validation.
6. Open a PR to `main` with `Closes #<issue-number>` in the body.

```sh
git checkout main
git pull
git checkout -b task/TXXX-short-description
```

Use `TXXX` as the issue or task number and keep the branch name short and descriptive.

## Issues, templates, and labels

Use the repository templates when opening a new issue:

- [Bug report](https://github.com/Krusty93/relego/issues/new?template=bug_report.md) issues default to the `bug` label.
- [Feature request](https://github.com/Krusty93/relego/issues/new?template=feature_request.md) issues default to the `enhancement` label.
- [Documentation](https://github.com/Krusty93/relego/issues/new?template=documentation.md) issues default to the `documentation` label.

Planned product work also uses feature labels such as `feature:003-highlight-parser`. Those labels map to the design artifacts in `specs/` and should stay aligned between the parent issue, its subtasks, and the final PR.

### Spec-kit workflow for tracked features

This repository already wires [GitHub speck-kit](https://github.com/github/spec-kit) into the local `/speckit.*` commands. Use that workflow only for tracked feature work under a `feature:00X-name` label, not for routine bug fixes, docs changes, or chores.

1. Create the Design subtask and the Implementation subtask under the parent feature issue.
2. Move the Design subtask to `In progress` before writing spec artifacts.
3. Run `/speckit.specify`, `/speckit.plan`, and `/speckit.tasks` in order.
4. Capture user or maintainer decisions at each step instead of letting the tool guess scope or behavior.
5. Treat the resulting `spec.md`, `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, and `tasks.md` files in `specs/00X/` as the feature design package, and open a PR.
6. Iterate on the design package until the implementation phase is ready, then merge to `main` before starting implementation.
7. After `tasks.md` is ready, create one implementation phase subtask per phase and implement them incrementally.
8. When a task is completed on a branch, mark it `[X]` in `tasks.md` on that same branch before pushing.

Do not add ad-hoc files under `specs/` or run spec-kit for work that is not tied to a tracked feature issue.

If a non-feature task does not match an existing label, ask a maintainer before starting broad work.

## Project workflow

Issues use these statuses: `Backlog`, `Ready`, `In progress`, `In review`, `Done`.

If you have project access:

- Move the issue to `In progress` before you start.
- Move it to `In review` when the PR is open.
- Move it to `Done` after merge.

Every PR should:

- Target `main`.
- Close its linked issue via `Closes #<issue-number>`.
- Carry the same label as the linked issue.
- Stay focused on one logical change.

For larger feature work, follow the spec-kit flow above instead of hand-editing a partial `specs/` package or opening an untracked implementation PR.

## Adding a new highlight source

Relego's import side is intentionally open for new highlight sources. The architecture is documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and the Kobo decision record in [docs/adr/008-kobo-reader-sqlite-source.md](docs/adr/008-kobo-reader-sqlite-source.md). In short: a source owns its own identity, detection, and reading logic, then feeds the same `ParseResult` model as every other source.

To add a source:

1. Implement `IHighlightSource` in `src/Relego.Cli/Sources/`.
2. Give it a stable `SourceDescriptor`, for example `new("readwise", "Readwise")`. The id is for reporting and logs only; do not branch on it.
3. Implement `Locate(string? userPath)` so the source owns its filename, directory, export, or device-detection rules and returns all probed locations in `SourceProbe`.
4. Implement `ReadAsync(...)` so it returns the shared `ParseResult` shape. For row-oriented sources, normalize raw records to `RawClipping` and run them through `HighlightAggregator.Aggregate(...)` so deduplication, note prefixing, and grouping stay consistent with Kindle and Kobo.
5. Register one line in `src/Relego.Cli/Program.cs`, for example `builder.Services.AddSingleton<IHighlightSource, ReadwiseSource>();`. Registration order is processing order when several sources are detected.
6. Add focused tests under `src/Relego.Tests/Sources/`. Include a small fixture in `src/Relego.Tests/Fixtures/` for test stability; add a docs-facing example under `docs/examples/` when it helps contributors or users understand the format.

Do not edit `HighlightSourceResolver`, `ClippingsImportWorkflow`, `ImportCommand`, or a central enum to add source-specific handling. There is no central enum by design: the resolver iterates the DI-registered sources and the workflow imports every resolved source with per-source failure isolation. Keeping that Open/Closed guarantee is part of the contributor contract.

## Development setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/)
- [Node.js](https://nodejs.org/) and `npm` only if you touch `src/landing`

### Common commands

```sh
dotnet build src/Relego.Server/Relego.Server.csproj
dotnet build src/Relego.Cli/Relego.Cli.csproj
dotnet test src/Relego.Tests/Relego.Tests.csproj
```

For landing page changes:

```sh
cd src/landing
npm install
npm run build
```

Docs-only changes usually only need a careful proofread and link/path check.

## Pull request guidelines

- Add or update tests when behavior changes.
- Update living docs when workflow, architecture, or user-facing behavior changes.
- Keep the PR description short and factual: what changed, why it changed, and how you validated it.
- Do not mix unrelated refactors or formatting changes into the same PR.

## Reporting bugs and proposing changes

- Bugs: use the bug report template and include your OS, Docker version, and `relego version` output.
- Features: describe the problem and expected outcome before implementation details.
- Docs: use the documentation template for wording fixes, content gaps, or process updates.

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to its terms.
