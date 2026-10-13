# Contract: Web UI (primary management surface, FR-010)

**Route**: `/app/sync` (new nav item **Sync**, D6) + AppShell status indicator + Import page pointer.
UI implementation must run through the `impeccable` skill (AGENTS.md) and reuse existing
`ui.tsx` components, TanStack Query, and the toast host.

## Screens and states

| State (`status`) | UI | Primary action |
|------------------|----|----------------|
| `not_connected` | Setup card with prerequisites + full disclosures **before** the connect button | Connect Kindle cloud |
| `awaiting_pairing` | Server URL + one-time code, extension install steps (including "choose your Amazon region in the extension"), regenerate | Cancel setup |
| `connected` | "Connected · region: United Kingdom (read-only) · last synced …", **Sync frequency** selector, **Next run** time (extension-reported) | Sync now, Full resync, Disconnect |
| `syncing` | Progress bar: phase, `booksDone/booksTotal`, new highlights; polls 2 s | — (read-only) |
| `completed` | Explicit completion: "Imported N new highlights" or "Nothing new" | Done |
| `auth_expired` | Attention banner (also AppShell dot) with steps; link to open Kindle notebook | Retry |
| `failed` | Failure report with code, plain-language detail, recovery steps | Retry, Full resync, use local import |
| `browser_not_reporting` | "Relego hasn't heard from your browser since …"; the chosen frequency is still shown, but no sync is claimed and no next run is promised; the extension runs at most one sync when the browser is available | Open extension help |
| `reminder_due` | In-app reminder: no new content for 45 days | Dismiss, Sync now |

Always visible on the page: **Coverage & limits** (cloud-only coverage, sideloaded/personal gaps,
publisher export truncation), **What this needs** (server running, Chrome/Firefox + extension,
Amazon sign-in), **Privacy** (data path: your browser → your server; Relego never sees your Amazon
password and stores no Amazon session), and a **local import fallback** link to `/app/import`.

## Sync frequency and region controls (FR-025, FR-028, FR-021)

- **Frequency selector** (Sync page, shown whenever a connection exists — `connected`, `syncing`,
  `completed`, failure/attention states — a native `<select>`/radio group labelled "Sync frequency") with
  **exactly ten options in this order**: 15 minutes, 30 minutes, 45 minutes, 1 hour, 2 hours,
  4 hours, 6 hours, 12 hours, 18 hours, 24 hours. Options come from
  `schedule.allowedIntervalMinutes`; the current value is `schedule.intervalMinutes` (default 6 hours).
  Saving calls `PUT /sync/{providerId}/schedule`; success shows the saved value immediately and
  announces it politely. While `schedule.applied` is false, **Next run** reads "Applies when your
  browser next reports" (the extension applies it within one 60 s heartbeat while the browser is open);
  once applied it shows `nextRunAt`. A 422 restores the previous value and shows the allowed list.
  The control is not editable during a pending save. Helper text: "Your browser runs the sync at this
  frequency; Relego's server does not schedule it."
- **Next run**: always visible with the selector ("Next run: today 14:30"), sourced from the extension's
  last report; when `browser_not_reporting`, it reads "will run when your browser is available". Never
  worded as a completed sync. The UI never calls the 60 s heartbeat a "sync".
- **Region**: shown **read-only** in the status area ("Region: Germany (lesen.amazon.de)") with the
  hint "Change region in the Relego browser extension". No region editor and no host/URL input exist
  in the web UI. Before pairing it reads "Region: chosen in the extension". The Sync page also states:
  "Supported regions: United States, Canada, United Kingdom, Germany, France, Italy, Spain, Brazil.
  Only these validated hosts are supported; other Amazon regions are not supported yet."
- Region change observed by the UI: status briefly shows a forced full read of the new region and,
  if needed, the existing `auth_expired` repair banner for the new region's Amazon sign-in.

## Accessibility and testing

- Status changes announced via `aria-live="polite"`; progress uses `role="progressbar"`.
- Playwright specs under `src/relego.web/tests/sync.spec.ts` with mocked API for every state in the
  table, plus: the selector lists exactly the ten options; each option round-trips to the mocked
  PUT, shows the pending-apply wording until `applied` is true, then shows Next run; a 422 reverts; region is read-only and no editable host field exists.
  `a11y.spec.ts` extended for the new route.
- Disconnect requires confirmation stating that imported highlights are kept.

## CLI (optional, lowest priority)

`relego status` prints one read-only line, e.g. `Kindle cloud: syncing (37/120 books)`, from
`GET /status.kindleCloud`. No other CLI sync management is in scope (FR-010).
