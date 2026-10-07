# Contributing to Relego

Thank you for your interest in contributing. Relego uses an issue-first workflow: work starts from a GitHub issue or a tracked spec package, and lands in a pull request that references it.

If you are changing user-facing behavior, review [README.md](README.md) and [docs/DX.md](docs/DX.md) first so the implementation, wording, and examples stay aligned.

## Minimum first contribution path

For a first contribution, prefer a small issue labeled `documentation` or `good first issue`.

1. Pick an existing issue, or open one with the appropriate template.
2. Comment on the issue so work is not duplicated.
3. Create a branch from `main`.
4. Make one focused change.
5. Run the smallest relevant validation.
6. Open a PR to `main` using the pull request template, closing the issue with `Closes #<issue-number>` in the body.

```sh
git checkout main
git pull
git checkout -b task/TXXX-short-description
```

Use `TXXX` as the issue or task number and keep the branch name short and descriptive.

## Issues, templates, and labels

Use the repository templates when opening a new issue:

- [Bug report](https://github.com/Krusty93/relego/issues/new?template=bug_report.md) issues default to the `bug` label.
- [Feature request](https://github.com/Krusty93/relego/issues/new?template=feature_request.md) issues default to `enhancement`.
- [Documentation](https://github.com/Krusty93/relego/issues/new?template=documentation.md) issues default to the `documentation` label.

Feature work is tracked as a spec package under `specs/` with one linked issue; feature issues carry the `enhancement` label. Add every issue to the GitHub Project kanban right after creating it.

### Spec Kit workflow for tracked features

This repository uses [GitHub Spec Kit](https://github.com/github/spec-kit) with the Copilot skills integration. Use Spec Kit for tracked feature work, not for routine bug fixes, docs changes, or chores. Run every spec-kit operation through the `specify` CLI; never hand-edit `.specify/` internals or generated artifacts.

PRDs in `docs/prds/` define milestones and contain no user stories. Once a PRD is merged to `main`, create its features as spec packages under `specs/` by running the workflow from the repository root:

```sh
specify workflow run speckit -i spec="Describe the feature to build"
```

The workflow pauses after specification and planning for review. Inspect or resume a paused run with:

```sh
specify workflow status
specify workflow status <run-id>
specify workflow resume <run-id>
```

1. Review at each checkpoint and capture user or maintainer decisions instead of letting the tool guess scope or behavior.
2. After `spec.md` is ready, create exactly one GitHub issue for the spec and add it to the kanban. Feature issues carry the `enhancement` label, and the issue links the spec on `main`:

   ```sh
   gh issue create --title "<feature name>" --label enhancement --body "Spec: https://github.com/Krusty93/relego/blob/main/specs/00X-name/spec.md"
   gh project item-add 2 --owner Krusty93 --url <issue-url>
   ```

3. Merge the spec package to `main` before implementation starts.
4. Implement one task per PR. `/speckit-implement` runs as designed; deliver the stack in the dependency and execution order from `tasks.md`.
5. When a task is completed on a branch, mark it `[X]` in `tasks.md` on that same branch before pushing.

The spec package is the single source of truth for the feature; issues and PRs link to it and never restate requirements. Do not add ad-hoc files under `specs/` or run spec-kit for work that is not tied to a tracked feature. Ask the user whether to record an ADR whenever a significant decision is made during spec generation.

If a non-feature task is not covered by an existing label, ask a maintainer before starting broad work.

## Project workflow

Every issue is added to the GitHub Project kanban right after creation.

Every PR should:

- Target `main`.
- Use `.github/pull_request_template.md` and reference the right issue: `Relates to #<spec issue>` for spec PRs, `Closes #<tracking issue>` for everything else.
- Stay focused on one task.

Stacked delivery uses the [`gh-stack`](https://github.com/github/gh-stack) extension (preinstalled in the dev container); layers follow the dependency and execution order in `tasks.md`:

```sh
gh stack init task/TXXX-short-description   # first task
gh stack add task/TXXX-short-description    # each following task
gh stack submit                             # open the chained PRs
gh stack sync                               # after merges
```

Parallel `[P]` tasks may run concurrently with parallel agents or separate git worktrees.

For larger feature work, follow the spec-kit flow above instead of hand-editing a partial `specs/` package.

## Adding a new highlight source

Relego's import side is intentionally open for new highlight sources. The architecture is documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and the Kobo decision record in [docs/adr/008-kobo-reader-sqlite-source.md](docs/adr/008-kobo-reader-sqlite-source.md). In short: a source owns its own identity, detection, and reading logic, then feeds the same `ParseResult` model as every other source.

To add a source:

1. Implement `IHighlightSource` in `src/Relego.Cli/Sources/`.
2. Give it a stable `SourceDescriptor`, for example `new("kobo", "Kobo")`. The id is for reporting and logs only; do not branch on it.
3. Implement `Locate(string? userPath)` so the source owns its filename, directory, export, or device-detection rules and returns all probed locations in `SourceProbe`.
4. Implement `ReadAsync(...)` so it returns the shared `ParseResult` shape. For row-oriented sources, normalize raw records to `RawClipping` and run them through `HighlightAggregator.Aggregate(...)` so deduplication, note prefixing, and grouping stay consistent.
5. Register one line in `src/Relego.Cli/Program.cs`, for example `builder.Services.AddSingleton<IHighlightSource, KoboSource>();`. Registration order is processing order when several sources are detected.
6. Add focused tests under `src/Relego.Tests/Sources/`. Include a small fixture in `src/Relego.Tests/Fixtures/` for test stability; add a docs-facing example under `docs/examples/` when it helps contributors or users understand the format.

Do not edit `HighlightSourceResolver`, `ClippingsImportWorkflow`, `ImportCommand`, or a central enum to add source-specific handling. There is no central enum by design: the resolver iterates the DI-registered sources and the workflow imports every resolved source with per-source failure isolation. Keeping that Open/Closed guarantee is part of the contributor contract.

## Development setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/)
- [GitHub CLI](https://cli.github.com/) with the [`gh-stack`](https://github.com/github/gh-stack) extension for stacked PRs; the dev container installs it automatically
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

- Fill in `.github/pull_request_template.md`: a short changes summary plus the related issue or spec.
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
