# Quickstart: Validating Kindle Background Synchronization

Validation guide only; implementation detail lives in `tasks.md`. Contracts:
[sync-api](contracts/sync-api.md), [extension profile](contracts/extension-notebook-profile.md),
[provider](contracts/cloud-sync-provider.md), [web UI](contracts/web-ui.md). Data:
[data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK, Node.js/npm, Docker (per `CONTRIBUTING.md`)
- Chrome or Firefox for manual end-to-end; **an Amazon account with Kindle-store books and
  highlights** (user-supplied; never put credentials in Relego or scripts)

## 1. Automated checks

```powershell
dotnet build src/Relego.slnx
dotnet test src/Relego.Tests/Relego.Tests.csproj
cd src/relego.web; npm ci; npx playwright install --with-deps chromium; npm run typecheck; npm test
cd ../relego.extension; npm ci; npm run typecheck; npm test   # parser fixtures + alarm (sync vs heartbeat)/auth unit tests
```

Expected: all green, including existing Kindle clippings, Kobo, and `/imports` tests (local
fallback regression, SC-007).

## 2. Manual end-to-end (maps to spec scenarios)

1. `docker compose up --build`; open `http://localhost:8080/app/sync`.
2. **Disclosures first (SC-006)**: coverage, sideloaded gap, export-limit truncation, prerequisites
   are visible before connecting.
3. Connect → copy the one-time pairing code → load the extension (unpacked) → enter server URL +
   code → approve the server-origin permission.
4. The extension opens the selected region's notebook page (e.g. `read.amazon.com/notebook`); sign in **on Amazon's page**. Confirm Relego
   never showed a password field (US1-1).
5. Watch progress, then the explicit completion state (US1-2, FR-005 a/b).
6. Trigger **Sync now** with nothing new → "completed, nothing new", not a false success (US1-3).
7. **Resync/idempotency**: run **Full resync**; `newHighlights = 0`; highlight count unchanged
   (US2-3, SC-003). Repeat after killing the browser mid-run and retrying.
8. **Auth expired**: sign out of Amazon in that browser; next cycle shows the repair state and
   AppShell indicator with steps; sign in again → sync resumes (US2-1/2, SC-004).
9. **Browser stopped**: close the browser for longer than the 5-minute staleness window →
   "Browser not reporting", never "syncing" (FR-012). Reopen it → at most one catch-up sync runs.
9a. **Frequency (US5)**: in the Sync page pick each of the ten values; the page shows it saved, then
   "Applies when your browser next reports" until the extension's next 60 s heartbeat, then the
   extension-reported Next run. In the extension's service-worker console confirm the `kindle-sync`
   alarm period equals the chosen minutes and `kindle-heartbeat` stays 60 s. Restart the server and the
   browser: the choice persists and the alarm is re-created. Confirm the server has no routine sync
   schedule (only the reminder job exists in Quartz). Submit 5 minutes via `PUT` → 422 listing the ten.
9b. **Region (US4)**: choose any of the eight regions (US, Canada, UK, Germany, France, Italy, Spain,
   Brazil) in the extension popup (no free-text host); the web page shows the region read-only with the
   "only validated hosts are supported" notice; Japan, China, Denmark, Ireland and Poland are absent
   and a submission naming them is rejected with the supported-region list.
10. **Contract break**: point the extension at the changed-layout fixture → actionable failure with
    recovery steps, no empty "success" (SC-008).
11. **Local fallback**: import `My Clippings.txt` at `/app/import`; works with sync disconnected and
    connected (US3-2, FR-009).
12. **Reminder**: with a test clock, advance 45 days without new content → in-app reminder (and an
    email when SMTP + delivery address exist) (US3-3, SC-005).
13. **Disconnect**: all highlights remain; token rejected afterwards (FR-015).

## 3. Security spot checks

- Server DB and logs contain no Amazon cookies/passwords/session tokens and no raw pairing token.
- Extension manifest lists only the declared permissions; no host permission is granted at install,
  and only allowlisted region hosts can be requested.
- `GET /sync/kindle-cloud` and `/jobs` output contain no page HTML.
