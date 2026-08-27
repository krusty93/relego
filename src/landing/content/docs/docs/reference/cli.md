---
title: Relego CLI
description: Install and use the Relego CLI, with every command and its accepted parameters.
eyebrow: Reference
sidebar:
  order: 1
---

## CLI Installation

CLI is available as Docker image as well as native binaries.

### Docker

Nothing to install. Use the `docker-compose` file to run the CLI as:

```sh
docker compose run --rm relego-cli <command>
```

### Windows

```powershell
winget install Krusty93.Relego
```

Or, without winget:

```powershell
irm https://raw.githubusercontent.com/Krusty93/relego/main/install.ps1 | iex
```

The installer detects your architecture and prints where `relego.exe` is saved.

### macOS/Linux

```sh
curl -fsSL https://raw.githubusercontent.com/Krusty93/relego/main/install.sh | sh
```

The installer prints where `relego` is saved. If `~/.local/bin` is not on your
`PATH`, add this to `~/.zshrc`, `~/.bashrc`, or `~/.profile` and open a new
terminal:

```sh
export PATH="$HOME/.local/bin:$PATH"
```

The rest of this page uses the short form.

## Using Docker with a reader

Docker cannot see a USB reader until you mount it read-only. Then pass the path
inside the container to `import`.

**Windows.** Copy `My Clippings.txt` beside `docker-compose.yml`, then:

```powershell
docker compose run --rm `
  -v "$(Get-Location):/kindle:ro" `
  relego-cli import "/kindle/My Clippings.txt"
```

To access a connected reader directly, follow Microsoft's guide to
[connecting USB devices to WSL](https://learn.microsoft.com/en-us/windows/wsl/connect-usb).

**macOS.** Kindle normally mounts at `/Volumes/Kindle`:

```sh
docker compose run --rm \
  -v "/Volumes/Kindle/documents:/kindle:ro" \
  relego-cli import "/kindle/My Clippings.txt"
```

**Linux.** Kindle normally mounts at `/media/$USER/Kindle`:

```sh
docker compose run --rm \
  -v "/media/$USER/Kindle/documents:/kindle:ro" \
  relego-cli import "/kindle/My Clippings.txt"
```

For Kobo, mount the device root and import `.kobo/KoboReader.sqlite`.

## CLI Command Reference

### Library

| Command | Accepted parameters | Description |
| --- | --- | --- |
| `relego import [path]` | Optional reader file path | Imports a detected Kindle or Kobo, or the file at `path` |
| `relego rename-book <id> <title>` | Numeric book ID; non-empty title | Renames a book so recaps use a clean title |
| `relego status` | None | Shows server health, library totals, delivery configuration, and the next recap |
| `relego --version` | None | Prints the CLI version |

### Schedule

| Command | Accepted parameters | Result |
| --- | --- | --- |
| `relego config schedule daily <HH:mm>` | `daily`; time from `00:00` to `23:59` | Delivers every day in the CLI machine's local time zone |
| `relego config schedule weekly <HH:mm>` | `weekly`; time from `00:00` to `23:59` | Delivers weekly in the CLI machine's local time zone |
| `relego config schedule show` | None | Prints the cadence, time zone, and current schedule |
| `relego config count <value>` | Integer from `1` to `15` | Sets highlights per recap; default is `5` |
| `relego config count show` | None | Prints the current recap size |

```sh
relego config schedule daily 07:30
relego config count 8
```

### Delivery addresses

| Command | Accepted parameters | Result |
| --- | --- | --- |
| `relego config email kindle <address>` | Valid Send-to-Kindle email address | Sends an EPUB to the Kindle address |
| `relego config email inbox <address>` | Valid email address | Sends an HTML recap to this inbox, including Kobo's normal delivery route |
| `relego config email inbox ""` | Empty string | Clears the inbox destination |
| `relego recap trigger` | None | Sends a recap immediately without changing the schedule |
| `relego config show` | None | Prints every saved setting |

Both delivery addresses can be set at the same time. For Kindle, use the
Amazon-provided `@kindle.com` address.

### Selection controls

| Command | Accepted parameters | Result |
| --- | --- | --- |
| `relego weight set <id> <weight>` | Numeric highlight ID; weight from `1` to `5` | Higher weights make a highlight more likely to return |
| `relego weight list` | None | Lists every highlight with a custom weight |
| `relego exclude add <type> <id>` | Type: `highlight`, `book`, or `author`; numeric ID | Stops that item appearing in future recaps |
| `relego exclude remove <type> <id>` | Type: `highlight`, `book`, or `author`; numeric ID | Returns an excluded item to future recaps |
| `relego exclude list` | None | Lists every exclusion |
