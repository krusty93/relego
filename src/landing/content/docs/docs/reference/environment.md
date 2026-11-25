---
title: Environment variables
description: The variables the Relego server reads at startup, and where to put them.
eyebrow: Reference
sidebar:
  order: 3
---

The server reads these at startup. Put them in a `.env` file next to
`docker-compose.yml`, or export them in the shell you launch Compose from.

:::caution
`.env` holds a live mail credential. Keep it out of version control.
:::


## Mail

| Variable | Required | Description |
| --- | --- | --- |
| `RELEGO_KINDLE_EMAIL` | For Kindle delivery | The Send-to-Kindle address recaps are mailed to |
| `RELEGO_SMTP_HOST` | Yes | Hostname of your SMTP relay |
| `RELEGO_SMTP_PORT` | Yes | Relay port, commonly `587` |
| `RELEGO_SMTP_USER` | Usually | Relay username |
| `RELEGO_SMTP_PASSWORD` | Usually | Relay password or API key |

## Usage telemetry

The server sends one anonymous heartbeat a day so the project can tell how many installs are running
and on which version. It carries an installation ID, the server version, and the CPU architecture, and
nothing else — [what leaves your server](https://github.com/krusty93/relego/blob/main/src/Relego.Server/Telemetry/TelemetryPayload.cs)
is the complete list. Countries are derived from the request IP, which is then discarded. There is no
account, no cookie, and no data from your highlights, books, or mail settings.

Telemetry is on by default. Turn it off with either variable; both are read at startup and need a
restart:

| Variable | Required | Description |
| --- | --- | --- |
| `DISABLE_TELEMETRY` | No | Set to `true` to stop the heartbeat |
| `DO_NOT_TRACK` | No | The cross-vendor opt-out; `1` or `true` stops the heartbeat too |

```yaml
services:
  relego-server:
    environment:
      DISABLE_TELEMETRY: "true"
```

The full disclosure is in [PRIVACY.md](https://github.com/krusty93/relego/blob/main/PRIVACY.md). The
decision behind it is recorded in
[ADR-010](https://github.com/krusty93/relego/blob/main/docs/adr/010-anonymous-usage-telemetry.md).

