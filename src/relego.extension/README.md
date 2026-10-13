# Relego browser extension

Manifest V3 extension (Chrome and Firefox) that reads the Kindle Notebook of the user's selected,
validated Amazon region inside the user's **own authenticated Amazon session** and pushes the parsed
highlights to the user's Relego server.

The extension owns routine sync timing with a browser alarm whose period is the user-chosen sync
frequency; a separate fixed 60-second heartbeat alarm reports liveness and picks up user-requested
commands. The Relego server never schedules routine syncs.

## Commands

```powershell
npm ci
npm run typecheck
npm test
npm run build   # writes a loadable unpacked extension to dist/
```

## Permissions

- `storage` and `alarms` only.
- **No host permission is granted at install.** `optional_host_permissions` lists the broad
  `http(s)` patterns Chrome requires before an origin can be requested at runtime; in practice the
  extension only ever requests the selected region's official Kindle notebook host and the user's own
  Relego server origin, and releases the previous region's access on a region change.
- No `cookies`, `webRequest`, `tabs`-wide or `<all_urls>` permission. The extension never reads or
  exports Amazon cookies, and page HTML never leaves the browser.

## Choosing a region

The toolbar popup is the only place a region is chosen. It offers the eight validated regions as a
fixed list — there is no free-text host field anywhere in the extension — and it asks for host access
only for the region selected, at the moment the user saves. A denied prompt, or a server call that
fails, leaves the connection reading the region it already read. The popup shows the connection state
the server reports, which stays the source of truth for the selected region.

See `specs/011-kindle-background-sync/` for the feature specification and contracts.
