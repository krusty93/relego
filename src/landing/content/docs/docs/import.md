---
title: Import highlights
description: Find the highlight file on your Kindle or Kobo, then move it into the Relego library.
stage: 1
sidebar:
  order: 1
subtitle: "The trip starts on the device, before Relego is involved at all. Your Kindle or Kobo writes highlights to a file, then Relego reads that file into its library."
---

## Kindle

When you underline a passage on a Kindle, the device appends it to a plain text
file at `documents/My Clippings.txt` in its internal storage. Every highlight,
note, and bookmark you have ever made is in there, in the order you made it.

A single entry looks like this:

```text
The Pragmatic Programmer (David Thomas & Andrew Hunt)
- Your Highlight on page 12 | Location 210-211 | Added on Sunday, 3 May 2026 09:14:02

Care About Your Craft
==========
```

Relego reads the quote, the book title, and the author. Bookmarks and empty
entries are skipped.

:::note
`My Clippings.txt` only contains highlights made on the device itself.
Highlights you made in the Kindle mobile or desktop apps live in Amazon's cloud
and are not in this file.
:::

## Kobo

Kobo stores highlights in a SQLite database at `.kobo/KoboReader.sqlite` on the
device. `.kobo` is a hidden folder, so you may need to enable hidden files in
your file manager to see it.

Relego reads the bookmark table and pairs each highlight with its book title and
author. You do not need any SQLite tooling installed, the CLI reads the file
directly.

## 1. Start the server

The server is where your highlights live. It is a single Docker container with a
SQLite database at `/data/relego.db`, one file on your disk that you can back
up by copying it.

From a checkout of the repository, or any directory containing the project's
`docker-compose.yml`:

```sh
docker compose --profile app up -d
```

Check it came up:

```sh
docker compose ps
```

Then open <http://localhost:8080>. That is the Relego web UI, served by the
server itself. If it loads, you are ready to import.

The server also runs the recap scheduler in the background. It does not need to
be reachable from the internet.

:::caution
The MVP server has no authentication. Do not expose its port to the public
internet. Run it on your own machine, your home network, or behind a VPN.
:::

### Mail settings can wait

The server reads its mail settings from the environment. Put them in a `.env`
file next to `docker-compose.yml`:

```ini
RELEGO_KINDLE_EMAIL=your-name@kindle.com
RELEGO_SMTP_HOST=smtp.your-relay.example
RELEGO_SMTP_PORT=587
RELEGO_SMTP_USER=relego
RELEGO_SMTP_PASSWORD=your-relay-password
```

You do not need working mail settings to import. Come back to this when you
reach [Deliver](/docs/deliver/), which covers choosing a relay; the
[environment variable reference](/docs/reference/environment/) has the full
list.

### Your data

| What | Where | Notes |
| --- | --- | --- |
| Highlight library | `/data/relego.db` inside the container | Mount a volume so it survives a container rebuild |
| Settings | Same database | Changed in the web UI or with `relego config`, not by editing the file |
| Logs | Container stdout | `docker compose logs -f relego-server` |

Nothing is sent anywhere except the recap emails you configure. There is no
telemetry, no account, and no cloud component.

## 2. Get your highlights in

There are two ways in, and both put highlights in the same library:

- **The web UI**, drag the file from your reader onto a page. Nothing to
  install. This is the shortest path, and the one to start with.
- **The command line**, one command with device auto-detection and something
  you can schedule. Worth installing once you import regularly.

Either way, the file you need is the one your reader already wrote, as described
above.

### Option 1 (The web UI)

With the server running, open <http://localhost:8080> and go to **Import**.

Drop in your reader's file, or click to browse for it:

- Kindle, `documents/My Clippings.txt`
- Kobo, `.kobo/KoboReader.sqlite`

Connect the reader over USB and the file is on it. On Kobo, `.kobo` is a hidden
folder, so turn on hidden files in your file manager first, or copy the file off
the device and upload that copy.

The page reports what it added when the upload finishes. Uploading the same file
twice does not create duplicates, so re-uploading after a few more reading
sessions is the normal way to keep the library current.

Files up to 64 MB are accepted, far more than a full clippings file.

### Option 2 (The command line)

The CLI talks to the same server over HTTP, so keep the server running. By
default it looks for `http://localhost:8080`; set `SERVER_URL` if yours lives
elsewhere.

#### Install it

You have three options. Docker needs no install at all; the native binary is
faster to run and easier to script.

**Docker.** Nothing to install. Every command in these docs has a
`docker compose run --rm relego-cli …` form.

**Windows.**

```powershell
winget install Krusty93.Relego
```

Or, without winget:

```powershell
irm https://raw.githubusercontent.com/Krusty93/relego/main/install.ps1 | iex
```

The installer detects your architecture and prints where `relego.exe` was saved.

**macOS and Linux.**

```sh
curl -fsSL https://raw.githubusercontent.com/Krusty93/relego/main/install.sh | sh
```

The installer prints where `relego` was saved. If `~/.local/bin` is not on your
`PATH`, add this to `~/.zshrc`, `~/.bashrc`, or `~/.profile` and open a new
terminal:

```sh
export PATH="$HOME/.local/bin:$PATH"
```

#### Import

Connect the reader over USB, wait for it to mount, then:

```sh
relego import
```

Relego looks for a connected Kindle or Kobo, reads the source file, and adds
anything new to the library. Running it twice does not create duplicates.

If auto-detection does not find the device, pass the path yourself:

```sh
relego import "/Volumes/Kindle/documents/My Clippings.txt"
```

#### Importing through Docker

The container cannot see your reader unless you mount it. Mount the device
read-only and give the CLI the path *inside* the container.

**Windows.** WSL does not see USB devices by default. The simplest route is to
copy `My Clippings.txt` into the directory holding `docker-compose.yml`, then:

```powershell
docker compose run --rm `
  -v "$(Get-Location):/kindle:ro" `
  relego-cli import "/kindle/My Clippings.txt"
```

To read the device directly instead, follow Microsoft's guide to
[connecting USB devices to WSL](https://learn.microsoft.com/en-us/windows/wsl/connect-usb).

**macOS.** Kindle mounts at `/Volumes/Kindle`:

```sh
docker compose run --rm \
  -v "/Volumes/Kindle/documents:/kindle:ro" \
  relego-cli import "/kindle/My Clippings.txt"
```

**Linux.** Kindle usually mounts at `/media/$USER/Kindle`:

```sh
docker compose run --rm \
  -v "/media/$USER/Kindle/documents:/kindle:ro" \
  relego-cli import "/kindle/My Clippings.txt"
```

For Kobo, mount the device root and point at `.kobo/KoboReader.sqlite` the same
way.

## No device handy?

The repository ships real sample files you can import instead, upload them in
the web UI, or:

```sh
relego import docs/examples/kindle-highlights.txt
relego import docs/examples/kobo-highlights.sqlite
```

They are enough to see a full recap end to end.

## Both devices at once

If a Kindle and a Kobo are both connected, `relego import` imports both in a
single run. If one source fails, Relego reports that failure and still completes
the other. The web UI takes one file at a time, upload them one after the
other.

## Next

You have a library. [Deliver your first recap →](/docs/deliver/)
