# ADR-006: Docker-Only Server Distribution

**Status:** Accepted
**Date:** 2026-03-31

## Context

The server must run as an always-on process on a home server, NAS, or Raspberry Pi. The target user is comfortable running Docker containers. The server requires persistent storage and environment-based configuration.

Alternatives considered:
- **Native packages (deb, rpm, brew)** — require platform-specific packaging pipelines and complicate dependency management.
- **systemd service / bare binary** — possible, but requires manual setup of persistence, restarts, and environment configuration.

## Decision

Distribute the server exclusively as a **Docker image** published to GitHub Container Registry (`ghcr.io/krusty93/relego.server`).

Configuration is passed entirely via environment variables:
- `KINDLE_EMAIL` — the user's Send-to-Kindle email address
- `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD` — outbound email credentials

The client keeps its default server address in `Server:Url` and allows a `SERVER_URL` environment override.

Data is persisted in a named Docker volume (`relego-data`).

## Consequences

- A single command (`docker run` or `docker-compose up`) fully deploys the server — no installation steps, no dependency management.
- Image provenance is verifiable through GitHub Artifact Attestations (`gh attestation verify`).
- Environment variables are a well-understood configuration pattern for containerized workloads.
- The user is responsible for backing up the Docker volume (`/data/relego.db`).
- No native package is provided for the server — Docker is the only supported server deployment method.
- The client CLI is distributed separately as a binary (see ADR-002) and does not require Docker on the user's laptop.
