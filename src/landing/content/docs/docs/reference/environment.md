---
title: Environment variables
description: The variables the Relego server reads at startup, and where to put them.
eyebrow: Reference
sidebar:
  order: 3
---

The server reads these at startup. Put them in a `.env` file next to
`docker-compose.yml`, or export them in the shell you launch Compose from.

## Mail

| Variable | Required | Description |
| --- | --- | --- |
| `RELEGO_KINDLE_EMAIL` | For Kindle delivery | The Send-to-Kindle address recaps are mailed to |
| `RELEGO_SMTP_HOST` | Yes | Hostname of your SMTP relay |
| `RELEGO_SMTP_PORT` | Yes | Relay port, commonly `587` |
| `RELEGO_SMTP_USER` | Usually | Relay username |
| `RELEGO_SMTP_PASSWORD` | Usually | Relay password or API key |

## CLI

| Variable | Description |
| --- | --- |
| `RELEGO_THEME` | Colour theme for the deprecated terminal UI: `dark` (default) or `light` |

## Runtime

| Variable | Description |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` normally. The demo profile expects `Development` |

## A working example

```ini
# .env (sits next to docker-compose.yml)
RELEGO_KINDLE_EMAIL=your-name@kindle.com
RELEGO_SMTP_HOST=smtp.your-relay.example
RELEGO_SMTP_PORT=587
RELEGO_SMTP_USER=relego
RELEGO_SMTP_PASSWORD=your-relay-password
```

And the demo relay, which needs no credentials at all:

```sh wrap
RELEGO_SMTP_HOST=smtp4dev RELEGO_SMTP_PORT=2525 docker compose --profile demo up -d
```

:::caution
`.env` holds a live mail credential. Keep it out of version control, the
project's `.gitignore` already excludes it.
:::
