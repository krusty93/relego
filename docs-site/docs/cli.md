---
sidebar_position: 3
---

# CLI reference

The `relego` CLI lets you import highlights and manage server settings from the terminal without opening a browser.

## Installation

### Docker (no install)

```sh
docker compose run --rm relego-cli <command>
```

### Binary

**Windows (winget):**
```powershell
winget install Krusty93.Relego
```

**Windows (installer):**
```powershell
irm https://raw.githubusercontent.com/Krusty93/relego/main/install.ps1 | iex
```

**macOS / Linux:**
```sh
curl -fsSL https://raw.githubusercontent.com/Krusty93/relego/main/install.sh | sh
```

## Commands

| Command | Description |
|---|---|
| `relego import [path]` | Import highlights from a detected Kindle or Kobo source |
| `relego status` | Show server status and next recap |
| `relego config show` | Show all current server settings |
| `relego config schedule <daily\|weekly> [HH:MM]` | Set recap schedule |
| `relego config schedule show` | Show current schedule |
| `relego config count show` | Show current highlights-per-recap setting |
| `relego config count <1-15>` | Set highlights per recap (default: 5) |
| `relego config email kindle <address>` | Set the Send-to-Kindle email address |
| `relego config email inbox <address>` | Set the inbox email for HTML recap delivery. Pass `""` to clear |
| `relego exclude add <highlight\|book\|author> <id>` | Exclude an entity from future recaps |
| `relego exclude remove <highlight\|book\|author> <id>` | Re-include an excluded entity |
| `relego exclude list` | List all exclusions |
| `relego rename-book <id> <title>` | Rename a book |
| `relego weight set <id> <1-5>` | Set highlight weight |
| `relego weight list` | Show weighted highlights |
| `relego recap trigger` | Trigger a recap immediately |
| `relego --version` | Print version |
| `relego --help` | Show help |

## Configuration

The CLI connects to the Relego server via the `SERVER_URL` environment variable (default: `http://localhost:8080`).

```sh
export SERVER_URL=http://192.168.1.10:8080
relego status
```

Or set it permanently in `appsettings.json`:

```json
{
  "Server": {
    "Url": "http://192.168.1.10:8080"
  }
}
```
