# Developer Experience Design — Relego

**Version:** 0.2 — Draft
**Date:** 2026-03-31
**Status:** Draft

---

## Overview

Relego consists of two components with distinct installation and usage patterns:

- **Server** (`relego-server`) — Always-on Docker container deployed on a home server, NAS, or Raspberry Pi. Handles scheduling, spaced repetition, recap composition, and email delivery.
- **Client CLI** (`relego`) — Installed on the user's laptop. Used to sync highlights from `My Clippings.txt` and manage settings.

The guiding DX principle: **zero friction after a one-time setup**. Onboarding requires one Docker command, one environment variable, one sync command.

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

All configuration is passed as environment variables to the server container. The client keeps its default server address in `Server:Url` and allows a runtime override via `SERVER_URL`.

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
Step 3 — Connect Kindle via USB
Step 4 — Sync highlights
```

```sh
relego sync /Volumes/Kindle/documents/My\ Clippings.txt
```

Expected output:
```
✓ Connected to server at http://192.168.1.10:8080
✓ Parsed 1,243 highlights from 47 books
✓ 1,198 new highlights imported (45 duplicates skipped)
→ Next recap: Sunday, Apr 5 at 18:00
```

If no path is specified, `relego sync` auto-detects the Kindle mount path. If not found, it prompts the user:

```sh
relego sync
```

```
⚠ Kindle not found at default paths.
  Kindle connected and mounted? Enter the path to My Clippings.txt, or press Enter to cancel:
  > /media/user/Kindle/documents/My Clippings.txt
✓ Connected to server at http://192.168.1.10:8080
✓ Parsed 1,243 highlights from 47 books
✓ 1,198 new highlights imported (45 duplicates skipped)
→ Next recap: Sunday, Apr 5 at 18:00
```

Total time to first recap: ~2 minutes.

---

## Day-to-day usage

### Sync new highlights (after connecting Kindle via USB)

```sh
relego sync          # Auto-detect Kindle mount path; prompts if not found
relego sync <path>   # Explicit path to My Clippings.txt
```

---

## Settings management

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
✗ File not found: /Volumes/Kindle/documents/My Clippings.txt
  Is your Kindle connected via USB?
  Looking for the file at a different path? Run: relego sync <path>
```

### Email delivery failed

```
✗ Failed to deliver recap to your-address@kindle.com
  Reason: SMTP authentication failed
  Check your KINDLE_EMAIL and SMTP settings on the server:
    docker exec relego-server env | grep SMTP
```

### Empty clippings file

```
⚠ No highlights found in My Clippings.txt
  This can happen if the file is empty or in an unexpected format.
  Expected format: Kindle's native My Clippings.txt (UTF-8, BOM prefix per entry)
```

---

## Full CLI reference

|                   Command                       |              Description                  |
|-------------------------------------------------|-------------------------------------------|
| `relego sync [path]`                             | Import highlights from `My Clippings.txt` |
| `relego status`                                  | Show server status and next recap         |
| `relego config schedule <daily\|weekly> [HH:MM]` | Set recap schedule                        |
| `relego config schedule show`                    | Show current schedule                     |
| `relego config count show`                       | Show current highlights-per-recap setting |
| `relego config count <1-15>`                     | Set highlights per recap (default: 5)     |
| `relego config kindle-email <address>`           | Set the Kindle delivery email address     |
| `relego exclude highlight <id>`                  | Exclude a highlight from all recaps       |
| `relego exclude book <title>`                    | Exclude all highlights from a book        |
| `relego exclude author <name>`                   | Exclude all highlights from an author     |
| `relego exclude remove highlight <id>`           | Re-include a highlight                    |
| `relego exclude remove book <title>`             | Re-include a book                         |
| `relego exclude remove author <name>`            | Re-include an author                      |
| `relego exclude list`                            | List all exclusions                       |
| `relego weight set <id> <1-5>`                   | Set highlight weight                      |
| `relego weight list`                             | Show weighted highlights                  |
| `relego version`                                 | Print version                             |

---

## Decisions

- `relego sync` auto-detects the Kindle mount path on macOS, Linux, and Windows. If not found, it prompts the user to enter the path interactively.
- Server authentication is not required — the server is assumed to be on a trusted local network.

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
