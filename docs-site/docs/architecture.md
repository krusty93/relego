---
sidebar_position: 4
---

# Architecture

Relego follows a client/server model with three independently deployable components.

```
Browser / CLI (user's device)
  │
  │  REST HTTP
  ▼
relego-server (:8080)           ← Docker container
  ├── REST API (ASP.NET Minimal API)
  ├── Scheduler (Quartz.NET)
  ├── SMTP sender (MailKit)
  └── SQLite (/data/relego.db)
        │
        │ SMTP
        ▼
  Send-to-Kindle or inbox email

relego-web (:8081)              ← Docker container (Nginx + React SPA)
  └── Calls relego-server via REST
```

## Components

### Server (`relego-server`)

The always-on backend. Runs as a Docker container on your home server, NAS, or Raspberry Pi. Responsibilities:

- Parse and store imported highlights
- Schedule and send recaps via SMTP
- Expose a REST API for the web UI and CLI

### Web UI (`relego-web`)

A React single-page application served by Nginx. Communicates directly with the server via REST over CORS. All management tasks — importing highlights, configuring settings, triggering recaps — are available here.

### CLI (`relego`)

A .NET self-contained binary (or Docker image) for users who prefer the terminal. Imports highlights from registered sources and exposes all server configuration commands. Install it on your laptop and point it at the server URL.

## Data model

All data lives in a single SQLite database at `/data/relego.db` inside the server container, backed by a Docker volume.

Key tables:

| Table | Contents |
|---|---|
| `users` | Delivery addresses |
| `books` | Imported books and authors |
| `highlights` | Individual highlight texts with weight and exclusion state |
| `settings` | Per-user recap schedule |
| `smtp_settings` | Outgoing mail server configuration |
| `recap_jobs` | Delivery history |

## Adding a new highlight source

The CLI imports through an open source registry. Each source implements `IHighlightSource`, owns a stable `SourceDescriptor`, and is registered with a single DI line. No central enum, no branching in the import workflow.

See [CONTRIBUTING.md](https://github.com/krusty93/relego/blob/main/CONTRIBUTING.md#adding-a-new-highlight-source) for the step-by-step guide.
