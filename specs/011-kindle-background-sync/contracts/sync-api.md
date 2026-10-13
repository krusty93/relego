# Contract: Sync REST API

**Spec**: FR-001–FR-012 | **Conventions**: JSON only; errors are RFC 9457 `application/problem+json`
with an actionable `detail`; validation errors are HTTP 422 (matches `/highlights/import`).
`{providerId}` is a registered provider id (`kindle-cloud`); unknown ids return 404 listing valid ids.

Two audiences:

- **Management** (web UI primary, CLI optional) — unauthenticated, consistent with ADR-004.
- **Extension channel** — `Authorization: Bearer <pairing token>`; 401 with
  `recovery: "re-pair"` for a missing/revoked token. The token is never logged.

## Management endpoints

| Method & path | Purpose | Success |
|---------------|---------|---------|
| `GET /sync/providers` | Registered providers, capabilities, reminder days | 200 |
| `GET /sync/{providerId}` | Status, schedule, region, active/last job, progress, failure report, reminder, disclosures | 200 |
| `POST /sync/{providerId}/connection` | Create connection; returns pairing token **once** | 201 |
| `PUT /sync/{providerId}/schedule` | Set sync frequency `{ "intervalMinutes" }` (one of the ten); **stores and returns it only** — the extension applies it on its next heartbeat; no server schedule is created | 200 |
| `GET /sync/{providerId}/regions` | Supported-region allowlist (read-only) for display in the UI and extension | 200 |
| `DELETE /sync/{providerId}/connection` | Disconnect (revokes token, keeps all highlights) | 204 |
| `POST /sync/{providerId}/commands` | Queue `sync` (retry) or `full_resync` | 202 |
| `POST /sync/{providerId}/reminders/{id}/dismiss` | Dismiss in-app reminder | 204 |
| `GET /sync/{providerId}/jobs?limit=20` | Recent jobs (history/failure reports) | 200 |

### `GET /sync/{providerId}` response

```json
{
  "providerId": "kindle-cloud",
  "status": "syncing",
  "connection": { "state": "active", "connectedAt": "…", "lastHeartbeatAt": "…", "browserReporting": true,
                  "region": { "id": "uk", "displayName": "United Kingdom", "host": "read.amazon.co.uk" } },
  "schedule": { "intervalMinutes": 360, "allowedIntervalMinutes": [15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440],
                "nextRunAt": "…", "pendingCommand": "none" },
  "currentJob": { "id": 41, "mode": "routine", "status": "running", "phase": "reading", "booksTotal": 120, "booksDone": 37, "highlightsNew": 212 },
  "lastCompletedJob": { "id": 40, "status": "completed_no_changes", "endedAt": "…" },
  "failure": null,
  "reminder": null,
  "disclosure": { "version": "1", "items": [ { "kind": "Sideloaded", "title": "…", "body": "…" } ] },
  "prerequisites": [ "Relego server running", "Chrome or Firefox with the Relego extension", "An active Amazon sign-in in that browser" ]
}
```

`status` ∈ `not_connected | awaiting_pairing | connected | syncing | completed | auth_expired | failed | browser_not_reporting | reminder_due`.
`failure` (when present): `{ "code", "detail", "recoverySteps": [...], "occurredAt" }`.
`disclosure` is always returned, including when not connected (SC-006). `connection.region` is
`null` until the extension pairs (the UI then says the region is chosen in the extension); the
`host` is **derived by the server** from the allowlist and is display-only. `schedule.nextRunAt` is
the next due time of the **extension's sync alarm** as last reported in a heartbeat (`null` until
reported or when not `active`; display-only); `schedule.applied` is `true` when the extension has
reported applying the stored `intervalMinutes`, else the UI shows a pending state;
`schedule.pendingCommand` is `none | sync | full_resync` (user-requested only, never created by a
routine schedule).

### `PUT /sync/{providerId}/schedule`

Body `{ "intervalMinutes": 120 }` → `200 { "intervalMinutes": 120, "applied": false, "nextRunAt": "…" }`.
Persists the value and returns it; `applied` flips to `true` after the extension's next heartbeat
reports it. **No Quartz job or trigger is created or changed.**
Any value outside `15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440` → **422** with detail listing the
ten allowed values; the stored value is unchanged. No active connection → **409**
("Connect Kindle cloud first"). Accepted while a job is running without affecting that job.

### `GET /sync/{providerId}/regions`

`200 [ { "id": "us", "displayName": "United States", "host": "read.amazon.com" }, { "id": "ca", … }, { "id": "uk", … }, { "id": "de", … }, { "id": "fr", … }, { "id": "it", … }, { "id": "es", … }, { "id": "br", … } ]`.
Japan and China (documented incompatible) and Denmark, Ireland and Poland (no dedicated notebook
host validated) are absent by design; the list is the exhaustive
allowlist for the current profile version and the response includes
`"disclosure": "Only these validated hosts are supported; other Amazon regions are unsupported until validated."`
(the web UI and extension show this text).

## Extension channel endpoints

| Method & path | Purpose |
|---------------|---------|
| `POST /sync/{providerId}/pair` | Exchange pairing token for activation; body `{ "profileVersion", "regionId" }` → connection `active`, response includes the stored `intervalMinutes` (default 360) for the extension to create its sync alarm. Re-pair with a new token keeps history, interval and (re-sent) region |
| `PUT /sync/{providerId}/region` | Change region from the extension popup; body `{ "regionId" }`. Same connection/token (no re-pair); running job → `interrupted`; `full_resync` queued; response `{ "region": {…}, "pendingCommand": "full_resync" }` |
| `POST /sync/{providerId}/heartbeat` | Liveness, fixed cadence **60 s**; body `{ "profileVersion", "appliedIntervalMinutes", "nextSyncDueAt" }`; response `{ "command": "none|sync|full_resync", "region": {…}, "intervalMinutes": 360, "heartbeatSeconds": 60 }` (consumes the single user-requested command). The extension re-creates its `kindle-sync` alarm when `intervalMinutes` differs from `appliedIntervalMinutes`. `heartbeatSeconds` is fixed and **never** derived from the sync frequency; a heartbeat never starts a routine sync |
| `POST /sync/{providerId}/jobs` | Start job (extension-initiated, incl. routine syncs fired by the sync alarm); body `{ "idempotencyKey", "mode", "trigger" }` with `trigger` ∈ `scheduled|manual|initial|retry|region_change` → `{ "jobId", "knownBooks": [{ "key", "highlightCount" }] }`; replay with same key returns the same job; if a job is already running, that job is returned (**no second concurrent job**, so overlapping/overdue alarm firings coalesce) |
| `POST /sync/{providerId}/jobs/{jobId}/batches` | Upload batch; idempotent per `batchIndex`; replay returns stored counts |
| `POST /sync/{providerId}/jobs/{jobId}/progress` | `{ "phase", "booksTotal", "booksDone" }` |
| `POST /sync/{providerId}/jobs/{jobId}/complete` | Explicit completion `{ "booksTotal", "booksDone", "truncatedBooks" }` |
| `POST /sync/{providerId}/jobs/{jobId}/fail` | `{ "code", "diagnostics": { "profileVersion", "check", "counts" } }` — diagnostics are validated to contain no free text from the page |

### Batch request

```json
{
  "batchIndex": 0,
  "books": [
    {
      "externalKey": "B00EXAMPLE",
      "title": "…", "author": "…",
      "truncated": false,
      "highlights": [ { "externalId": "…", "text": "…", "note": null, "location": "123-125", "color": "yellow", "addedOn": null } ]
    }
  ]
}
```

Batch response = existing `SyncResponse` shape (`newHighlights`, `duplicateHighlights`,
`newBooks`, `newAuthors`) plus `batchIndex` and `replayed: bool`.

### Status codes

- 200/201/202/204 as above; **422** validation (empty text, oversize batch, bad mode); **401**
  token; **404** unknown provider/job; **409** job already terminal or `full_resync` while a job is
  running (message names the running job); **413** body > 2 MB.
- A `trigger: scheduled` job start while another job is running is **not** an error: it returns 200 with the running job. A `full_resync` start while a job is running is 409 naming the running job.
- **422** also covers an unknown/excluded `regionId` (including `jp`, `cn`, `dk`, `ie`, `pl`, or any host-like string);
  the detail lists the supported region IDs. Hosts are never accepted from clients.
- A job may only reach `completed_*` via `complete` with matching counts; otherwise server marks it
  `interrupted` after a stale window (default 15 min without progress).

## Invariants (testable)

1. Replaying any request with the same idempotency key / batch index never changes totals.
2. Highlight rows are only inserted, never updated or deleted by any endpoint here.
3. No response or log contains the token, Amazon cookies, or page HTML.
4. `DELETE connection` leaves `highlights`, `books`, `authors` untouched.
5. A `full_resync` over fully imported content yields `newHighlights = 0`, status `completed_no_changes`.
6. For each of the ten intervals, `PUT schedule` stores and returns it; any other value changes
   nothing; the value survives server restart; the heartbeat response carries it; **no Quartz job or
   trigger exists for routine sync** (only the reminder job is registered).
7. Starting a job while one is running returns the running job; no number of overdue/overlapping
   extension-initiated starts yields more than one running job, and no `last_sync_completed_at`
   changes while the browser is silent (no claimed sync).
8. A `PUT region` never deletes highlights, keeps connection id/token/interval, and leaves exactly
   one pending `full_resync`.

## Existing endpoints (unchanged)

`POST /highlights/import`, `POST /imports` (local Kindle/Kobo upload), `GET /status`. Optionally,
`GET /status` gains a `kindleCloud` summary object (`status`, `lastSyncAt`, `nextRunAt`, `region`) for the AppShell
indicator and optional CLI line — additive only.
