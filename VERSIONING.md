# Versioning guide

This project uses independent semantic versioning for each component (`core`, `cli`, `server`).
Version numbers follow the [Semantic Versioning](https://semver.org/) 2.0.0 specification.
Tag creation and GitHub Releases are fully automated for CLI and server projects.

## How to release a new version

### 1. In your feature branch, bump the version in each modified component

Update the centralized version property for every modified component in `src/PackageVersions.props`:

```xml
<RelegoCliVersion>1.3.0</RelegoCliVersion>
```

Relevant files:

| Component | Central property      |
|-----------|-----------------------|
| `core`    | `RelegoCoreVersion`   |
| `cli`     | `RelegoCliVersion`    |
| `server`  | `RelegoServerVersion` |

Decide the bump level yourself (patch / minor / major) — no commit message format is required.
Bump the version whenever you modify a component, even if the change is not user-facing (e.g. refactor, bug fix, internal API change) — this ensures accurate version tracking and release notes.

The embedded web UI (`src/relego.web/`) ships with Server, so web UI changes require a `RelegoServerVersion` bump. Its private npm metadata remains fixed at version `1.0.0` and is not independently released or tagged.

### 2. Commit and open a PR

```bash
git add src/PackageVersions.props
git commit -m "your commit message"
git push origin feature/your-branch
```

The CI will check that every modified component has a bumped version and post a comment on the PR with the result. The merge is blocked until all checks pass.

### 3. Merge the PR — everything else is automatic

After the merge:

- `post-merge.yml` resolves the central version properties for `core`, `cli`, and `server` and creates a git tag for each component whose bumped version does not yet have a matching tag (format: `<component>/v<version>`)
- The tag push triggers `release.yml`, which publishes one GitHub Release per component tag using the squash-merge commit titles since the previous component tag, with a link to the originating PR when available
- The published GitHub Release triggers `deploy-cli.yml` (Docker image) and `deploy-server.yml` (Docker image)

`core` is version-tagged for dependency tracking but is excluded from GitHub release notes and release pages.

## Tag format

Tags follow the pattern `<component>/v<version>`:

```text
core/v1.1.0
cli/v1.3.0
server/v2.0.0
```

## Adding a new component

1. Add a `<Name>Version` property to `src/PackageVersions.props` and reference it from `src/<Name>/<Name>.csproj` with `<Version>$(<Name>Version)</Version>`
2. Add `dotnet sln src/Relego.slnx add src/<Name>/<Name>.csproj`
3. Ensure the project folder and `.csproj` file use the `Relego.<Component>` naming pattern so the release metadata remains consistent
4. Add the component to `.github/actions/discover-versioned-components/action.yaml`, including its centralized version property; only listed components are released or tagged
5. If the component should be publish a GitHub Release page, update triggers in `.github/workflows/release.yaml` to include it
6. Create the deployment workflow for the specific component, following the triggers in `deploy-server.yml`

## Required one-time setup (repository owner)

| Secret          | Scope                                   | Used by                                                                    |
| --------------- | --------------------------------------- | -------------------------------------------------------------------------- |
| `RELEASE_TOKEN` | fine-grained PAT, `contents:read+write` | `post-merge.yml` — pushes tags in a way that triggers downstream workflows |

Branch protection on `main`:

- Require PR before merging
- Required status checks: `Build & Test`, `Version Bump Check`

> **Note:** `GITHUB_TOKEN` cannot trigger other workflows when used to push tags, which is why `RELEASE_TOKEN` is required.
