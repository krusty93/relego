# Developer Experience Design — Relego

**Version:** 0.2 — Draft
**Date:** 2026-03-31
**Status:** Draft

---

## Overview

Relego consists of two deployable components with distinct installation and usage patterns:

- **Server** (`relego-server`) — Always-on Docker container deployed on a home server, NAS, or Raspberry Pi. Handles scheduling, spaced repetition, recap composition, email delivery, and serves the web UI.
- **Client CLI** (`relego`) — Installed on the user's laptop. Used to import highlights directly from a connected device (Kindle and Kobo today), to manage settings from the terminal, and for scripting.

The guiding DX principle: **zero friction after a one-time setup**. Onboarding requires one server start, one delivery destination, one import.

---

## Installation

### Server

```sh
docker network create relego

docker run -d \
  --name relego-server \
  --restart unless-stopped \
  -e KINDLE_EMAIL=your-address@kindle.com \
  -e SMTP_HOST=smtp.example.com \
  -e SMTP_PORT=587 \
  -e SMTP_USER=user@example.com \
  -e SMTP_PASSWORD=yourpassword \
  -p 8080:8080 \
  -v relego-data:/data \
  --network relego \
  ghcr.io/krusty93/relego.server:latest
```

That's it. The server is running and will start sending recaps on the default schedule (daily at 18:00 client's local time).

### Web UI

The server image and self-contained server archive include the Vite production build. Open <http://localhost:8080> after starting `relego-server`; the web UI and API share the same origin and require no additional browser configuration.

Once SMTP is saved from the Settings page it lives in the database and the `SMTP_*` environment variables stop being read; they only seed an empty configuration on first boot.

### Client CLI

**Option A — Docker (no install required):**
```sh
docker run --rm -e SERVER_URL=http://192.168.1.10:8080 ghcr.io/krusty93/relego.cli:latest <command>
```

**Option B — Download binary:**
```sh
# macOS (Apple Silicon)
curl -L https://github.com/Krusty93/relego/releases/latest/download/relego-darwin-arm64 -o /usr/local/bin/relego
chmod +x /usr/local/bin/relego

# macOS (Intel)
curl -L https://github.com/Krusty93/relego/releases/latest/download/relego-darwin-amd64 -o /usr/local/bin/relego
chmod +x /usr/local/bin/relego

# Linux
curl -L https://github.com/Krusty93/relego/releases/latest/download/relego-linux-amd64 -o /usr/local/bin/relego
chmod +x /usr/local/bin/relego

# Windows (via winget)
winget install Krusty93.Relego
```

---

## Configuration

Server bootstrap configuration is passed as environment variables to the server container. User-facing settings, including Kindle and inbox delivery addresses, are managed through the CLI and stored server-side. The client keeps its default server address in `Server:Url` and allows a runtime override via `SERVER_URL`.

```sh
docker run -d \
  --name relego-server \
  --restart unless-stopped \
  -e KINDLE_EMAIL=your-address@kindle.com \
  -p 8080:8080 \
  -v relego-data:/data \
  ghcr.io/krusty93/relego.server:latest
```

The client automatically connects to `http://localhost:8080`. If your server runs on a different host or port, set `SERVER_URL` on the client side:

```sh
# ~/.zshrc or ~/.bashrc (macOS/Linux)
export SERVER_URL=http://192.168.1.10:8080

# Windows (PowerShell profile)
$env:SERVER_URL = "http://192.168.1.10:8080"
```

No other configuration is required to get started.

---

## Onboarding flow (first-time setup)

```
Step 1 — Deploy server (see above)
Step 2 — Set SERVER_URL in your shell profile if the server is not on localhost:8080
Step 3 — Configure at least one delivery destination
Step 4 — Connect a Kindle or Kobo via USB
Step 5 — Import highlights
```

```sh
relego config email kindle your-address@kindle.com
relego import /Volumes/Kindle/documents/My\ Clippings.txt
```

Expected output:
```
✓ Connected to server at http://192.168.1.10:8080
✓ Detected Kindle source at /Volumes/Kindle/documents/My Clippings.txt
✓ Parsed 1,243 highlights from 47 books
✓ 1,198 new highlights imported (45 duplicates skipped)
→ Next recap: Sunday, Apr 5 at 18:00
```

Kobo users use the regular inbox email channel because Kobo has no Send-to-Kindle-style address:

```sh
relego config email inbox you@example.com
relego import /Volumes/KOBOeReader/.kobo/KoboReader.sqlite
```

If no path is specified, `relego import` probes the default Kindle and Kobo mount locations. Passing a mounted device root is also valid; each registered source owns its own detection rules. For explicit Kindle file paths, Relego routes any existing `.txt` file to the Kindle parser; the `My Clippings.txt` filename is only required for device auto-detection.

Total time to first recap: ~2 minutes.

---

## Day-to-day usage

### Import new highlights (after connecting a reading device via USB)

```sh
relego import          # Auto-detect Kindle and Kobo mount paths
relego import <path>   # Explicit source file or mounted device root
```

When both a Kindle and a Kobo are connected, Relego imports both in one run and reports each source separately. If one source fails, the other still imports.

Without a CLI install, open the web UI's **Import** page and drop `My Clippings.txt` or `KoboReader.sqlite` onto it. The server sniffs the format, parses it with the same `Relego.Core` code the CLI uses, and reports per book what was added and what was already there. This is the only import path that works when the device is attached to a machine that has no Relego binary — for example a phone or a locked-down work laptop.

---

## Settings management

### Email addresses

Both delivery destinations are optional — at least one must be set for recap delivery to succeed.

```sh
# Set the Send-to-Kindle address (optional if an inbox address is set)
relego config email kindle your-address@kindle.com

# Set the inbox address for HTML recap delivery (optional if a Kindle address is set)
relego config email inbox you@example.com

# Clear the inbox address
relego config email inbox ""

# Show all current settings (includes both addresses)
relego config show
```

### Schedule

```sh
relego config schedule daily          # Send recap every day at 18:00 (default time)
relego config schedule daily 08:00    # Send recap every day at 08:00
relego config schedule weekly         # Send recap every Sunday at 18:00
relego config schedule weekly 20:00   # Send recap every Sunday at 20:00
relego config schedule show           # Print current schedule
```

### Exclude highlights / books / authors

```sh
# Exclude a specific highlight by ID
relego exclude highlight <id>

# Exclude all highlights from a book
relego exclude book "The Pragmatic Programmer"

# Exclude all highlights from an author
relego exclude author "David Foster Wallace"

# Re-include a previously excluded highlight
relego exclude remove highlight <id>

# Re-include a previously excluded book
relego exclude remove book "The Pragmatic Programmer"

# Re-include a previously excluded author
relego exclude remove author "David Foster Wallace"

# List all exclusions
relego exclude list
```

### Highlight weights

```sh
# Set weight for a highlight (default: 1, range: 1–5)
relego weight set <id> 3

# Show weight distribution across highlights
relego weight list
```

### Highlights per recap

```sh
relego config count 5      # Show 5 highlights per recap (default: 5, min: 1, max: 15)
relego config count show   # Print current setting
```

### Status

```sh
relego status
```

```
Server:       http://192.168.1.10:8080 ✓ online
Highlights:   1,198 total · 12 excluded · 34 weighted
Highlights/recap: 5 (default)
Last recap:   Mar 30, 2026 at 18:00 (3 highlights delivered)
Next recap:   Apr 5, 2026 at 18:00
Schedule:     weekly (Sunday at 18:00)
```

---

## Error messages

Errors are actionable — they tell the user exactly what to do.

### Server unreachable

```
✗ Cannot connect to server at http://192.168.1.10:8080
  Is the server running? Check with: docker ps | grep relego-server
  Is SERVER_URL set correctly? Current value: http://192.168.1.10:8080
```

### File not found

```
✗ No Kindle or Kobo source detected.
  Checked: /Volumes/Kindle/documents/My Clippings.txt, /Volumes/KOBOeReader/.kobo/KoboReader.sqlite
  Looking for the file at a different path? Run: relego import <path>
```

### Email delivery failed

```
✗ Failed to deliver recap to your-address@kindle.com
  Reason: SMTP authentication failed
  Check your delivery address and SMTP settings on the server:
    docker exec relego-server env | grep SMTP
```

### Empty source

```
⚠ No highlights found in the source file.
  This can happen if the source is empty or in an unexpected format.
  Expected format: Kindle clippings text export (.txt) or Kobo KoboReader.sqlite
```

---

## Full CLI reference

|                   Command                              |              Description                                                     |
|--------------------------------------------------------|------------------------------------------------------------------------------|
| `relego`                                               | Open interactive TUI                                                         |
| `relego import [path]`                                 | Import highlights from a detected Kindle or Kobo source                      |
| `relego status`                                        | Show server status and next recap                                            |
| `relego config show`                                   | Show all current server settings                                             |
| `relego config schedule <daily\|weekly> [HH:MM]`       | Set recap schedule                                                           |
| `relego config schedule show`                          | Show current schedule                                                        |
| `relego config count show`                             | Show current highlights-per-recap setting                                    |
| `relego config count <1-15>`                           | Set highlights per recap (default: 5)                                        |
| `relego config email kindle <address>`                 | Set the Send-to-Kindle email address                                         |
| `relego config email inbox <address>`                  | Set the inbox email address for HTML recap delivery. Pass `""` to clear      |
| `relego exclude add <highlight\|book\|author> <id>`    | Exclude an entity from future recaps                                         |
| `relego exclude remove <highlight\|book\|author> <id>` | Re-include an excluded entity                                                |
| `relego exclude list`                                  | List all exclusions                                                          |
| `relego rename-book <id> <title>`                      | Rename a book                                                                |
| `relego weight set <id> <1-5>`                         | Set highlight weight                                                         |
| `relego weight list`                                   | Show weighted highlights                                                     |
| `relego recap trigger`                                 | Trigger a recap immediately                                                  |
| `relego --version`                                     | Print version                                                                |

---

## Decisions

- `relego import` auto-detects Kindle and Kobo sources on macOS, Linux, and Windows. Explicit Kindle file paths are routed by `.txt` extension, while auto-detection still probes the device's `My Clippings.txt` path. Each source owns its own detection rules, and the resolver imports every detected source with per-source failure isolation.
- Parsing and the source registry live in `Relego.Core`, so the CLI (device attached) and the server (file uploaded) share one implementation. Adding a source adds it to both.
- The web UI ships as static files with no build-time configuration and calls same-origin API paths. `dotnet publish` builds it through the server's JavaScript project reference, so Docker and executable releases contain the same UI/API artifact.
- Server authentication is not required — the server is assumed to be on a trusted local network. The web UI inherits that assumption and must not be exposed to the public internet.

---

## Local development (web UI)

```sh
# terminal 1 — API
cd src/Relego.Server
RELEGO_WEB_ROOT=../relego.web/dist dotnet run

# terminal 2 — UI with hot reload
cd src/relego.web
npm ci
npm run dev            # http://localhost:5173
```

For local frontend development, Vite proxies API routes to the server. The React source lives in `src/relego.web`; run `npm ci` and `npm run dev` there. For a complete executable-style output, run `dotnet publish src/Relego.Server`: the server project references `relego.web.esproj`, which builds Vite and includes its `dist` assets in the published server's `wwwroot` directory. For production-like local testing without publishing, build `src/relego.web` and set `RELEGO_WEB_ROOT` to its `dist` directory.

```sh
npm run typecheck      # tsc --noEmit over src, tests and configs
npm run build          # typecheck + production bundle into dist/
npm test               # Playwright: builds the SPA, boots the API, seeds fixtures, runs everything
npm run test:a11y      # axe-core sweep only
```

`npm test` starts `relego-server` itself against a throwaway SQLite file in the temp directory, so it never touches your development database. Existing servers on ports 8080 and 5173 are reused outside CI.

---

## Local development (VS Code)

### Configuration philosophy

All application settings live in `appsettings.*.json` — never in `.vscode/launch.json`. The launch configuration contains only the **minimum bootstrapping** needed by the .NET hosting framework:

| File | Purpose |
|------|---------|
| `launch.json` | Sets `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` to `Development` (selects which config files to load) |
| `appsettings.json` | Base settings shared across all environments |
| `appsettings.Development.json` | Dev-only overrides: URLs, ports, logging levels, local SMTP, etc. |
| `Properties/launchSettings.json` | Used by `dotnet run` (not the debugger); keep in sync with appsettings for consistency |

### Changing a setting

Edit `appsettings.Development.json` in the relevant project — no need to touch `.vscode/`.

**Examples:**

- Change the server port → `src/Relego.Server/appsettings.Development.json` → `"Urls": "http://localhost:9090"`
- Change SMTP settings → same file, `"Smtp"` section
- Change CLI log level → `src/Relego.Cli/appsettings.Development.json` → `"Serilog"` section

### Running in debug

Press **F5** and select `Relego.Server` or `Relego.Cli`. Both configurations automatically load `appsettings.Development.json` thanks to the environment variable set in `launch.json`.
