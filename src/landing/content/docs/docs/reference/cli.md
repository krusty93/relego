---
title: CLI commands
description: Every Relego CLI command, its accepted parameters, and where it fits in the round trip.
eyebrow: Reference
sidebar:
  order: 1
---

Every command works the same whether you installed the binary or run it through
Docker. The Docker form is the same command with a prefix:

```sh
relego status
docker compose run --rm relego-cli status
```

The rest of this page uses the short form.

## Command notation

`<value>` is required. `[value]` is optional. The CLI uses your local
`http://localhost:8080` server unless you set `SERVER_URL`.

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

## Library

| Command | Accepted parameters | Description |
| --- | --- | --- |
| `relego import [path]` | Optional reader file path | Imports a detected Kindle or Kobo, or the file at `path` |
| `relego rename-book <id> <title>` | Numeric book ID; non-empty title | Renames a book so recaps use a clean title |
| `relego status` | None | Shows server health, library totals, delivery configuration, and the next recap |
| `relego --version` | None | Prints the CLI version |

## Configuration and delivery

The web interface is the normal place to set these values. These commands are
useful when you automate Relego or prefer a terminal. Relego stores them in its
database, so they persist across restarts. The defaults are a daily recap at
`18:00` in the server's local time zone and five highlights per recap.

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
