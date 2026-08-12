---
title: CLI commands
description: Every Relego CLI command, what it does, and where it fits in the round trip.
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

## Library

| Command | Description |
| --- | --- |
| `relego import [path]` | Import highlights from a detected Kindle or Kobo source, or from an explicit file path |
| `relego rename-book <id> <title>` | Rename a book so recaps use a clean title |
| `relego status` | Show server status and when the next recap is due |
| `relego --version` | Print the CLI version |

## Selection

| Command | Description |
| --- | --- |
| `relego config schedule <daily\|weekly> [HH:MM]` | Set the recap schedule |
| `relego config schedule show` | Show the current schedule |
| `relego config count <1-15>` | Set how many highlights go into a recap (default 5) |
| `relego config count show` | Show the current recap size |
| `relego weight set <id> <1-5>` | Weight a highlight so it returns more often |
| `relego weight list` | List every weighted highlight |
| `relego exclude add <highlight\|book\|author> <id>` | Stop an entity appearing in future recaps |
| `relego exclude remove <highlight\|book\|author> <id>` | Undo an exclusion |
| `relego exclude list` | List every exclusion |

## Delivery

| Command | Description |
| --- | --- |
| `relego config email kindle <address>` | Set the Send-to-Kindle address |
| `relego config email inbox <address>` | Set the inbox address for the HTML recap. Pass `""` to clear it |
| `relego recap trigger` | Send a recap immediately, without touching the schedule |

## Everything

| Command | Description |
| --- | --- |
| `relego config show` | Print every current server setting |

:::note
Running `relego` with no arguments opens a deprecated interactive terminal UI.
It is being removed; use the commands above.
:::
