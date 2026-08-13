---
title: Import highlights
description: Find the highlight file on your Kindle or Kobo, then move it into the Relego library.
stage: 1
sidebar:
  order: 1
subtitle: "The trip starts on the device, before Relego is involved at all. Your Kindle or Kobo writes highlights to a file, then Relego reads that file into its library."
---

## Your reader's file

| Reader | File to import | Note |
| --- | --- | --- |
| Kindle | `documents/My Clippings.txt` | Contains device highlights, notes, and bookmarks |
| Kobo | `.kobo/KoboReader.sqlite` | `.kobo` is a hidden folder |

Relego reads the quote, book title, and author. You do not need SQLite tooling
for Kobo, upload the file as-is.

:::note
`My Clippings.txt` does not include highlights made in Kindle mobile or desktop
apps. Those remain in Amazon's cloud.
:::

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

The web interface is the standard way to import. The command line is for
scheduled or scripted imports, or if you simply prefer a terminal.

### In the web interface

With the server running, open <http://localhost:8080> and go to **Import**.

Drop in the file listed above, or click to browse for it. Connect the reader
over USB first. For Kobo, enable hidden files in your file manager or copy the
database off the device before uploading it.

The page reports what it added when the upload finishes. Uploading the same file
twice does not create duplicates, so re-uploading after a few more reading
sessions is the normal way to keep the library current.

Files up to 64 MB are accepted, far more than a full clippings file.

![Relego's web interface open to the Import page, showing the Kindle and Kobo file upload area.](/images/docs/relego-web-import.webp)

### Prefer the command line?

The CLI talks to the same server over HTTP. Use it to automate regular imports
or when a terminal suits your workflow better. Keep the server running; by
default the CLI looks for `http://localhost:8080`. Set `SERVER_URL` if yours
lives elsewhere.

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

If you run the CLI through Docker, mount the reader before importing. See
[Using Docker with a reader](/docs/reference/cli/#using-docker-with-a-reader).

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
