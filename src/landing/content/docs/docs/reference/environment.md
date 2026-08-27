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
