---
title: Troubleshooting
description: Symptoms you are likely to hit, and what actually fixes them.
eyebrow: Reference
sidebar:
  order: 4
---

Grouped by the stage of the round trip where things went wrong.

## Import

### `relego import` finds no device

Confirm the reader is mounted and browsable in your file manager first. Kindles
sometimes mount in charge-only mode, unlock the device after plugging it in.
If it is mounted and still not detected, pass the path explicitly:

```sh
relego import "/Volumes/Kindle/documents/My Clippings.txt"
```

### The Kobo file is not where the docs say

`.kobo` is a hidden directory. Enable hidden files in your file manager, or list
it from a terminal:

```sh
ls -a /media/$USER/KOBOeReader
```

### Nothing imports through Docker

The container cannot see your reader unless you mounted it. Check that the `-v`
flag points at a real host path and that the path you pass to `import` is the
path *inside* the container. See [Import](/docs/import/).

### Highlights I made in the Kindle app are missing

Only highlights made on the device itself are written to `My Clippings.txt`.
App highlights stay in Amazon's cloud and are not available to Relego.

## Server

### Commands error out or hang

The server is probably not running:

```sh
docker compose ps
docker compose logs -f relego-server
```

### The library emptied after a rebuild

The database at `/data/relego.db` was not on a mounted volume, so it went with
the container. Re-import, and check the volume mapping in `docker-compose.yml`.

## Recaps

### `No eligible highlights available`

Either nothing has been imported, or everything eligible is excluded:

```sh
relego status
relego exclude list
```

### The send succeeds but nothing reaches the Kindle

Almost always Amazon's approved sender list. The address your relay sends
**from** must be listed under Manage Your Content and Devices → Preferences →
Personal Document Settings. See [Select and deliver](/docs/select/).

### The relay rejects the login

Personal Gmail and Outlook accounts no longer accept SMTP password auth. Use a
relay with a free tier, the options are listed in
[Select and deliver](/docs/select/).

### Nothing appears in smtp4dev

Check that the `demo` profile is up, and that `relego-server` is running with
`ASPNETCORE_ENVIRONMENT=Development`, `RELEGO_SMTP_HOST=smtp4dev`, and
`RELEGO_SMTP_PORT=2525`.

### The smtp4dev page is blank

It renders a moment after load. Refresh once.

## Still stuck

Open an issue at
[github.com/Krusty93/relego](https://github.com/Krusty93/relego/issues) with the
command you ran and the relevant lines from `docker compose logs relego-server`.
