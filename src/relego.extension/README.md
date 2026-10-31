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
npm test          # also packages both browsers, to assert on the real artifacts
npm run build     # writes a loadable unpacked Chrome extension to dist/
npm run package   # writes both release packages to release/
```

## Installing the extension

The extension is distributed as a package attached to its GitHub Release. Neither store listing is
live yet, so both browsers are loaded from a package or from a local build.

### Chrome, Edge, and other Chromium browsers

1. Download `relego-extension-<version>-chrome.zip` from the [latest release][releases].
2. Unzip it into a folder you intend to keep — Chrome loads the unpacked folder, not the zip.
3. Open `chrome://extensions`, turn on **Developer mode**, choose **Load unpacked**, and select the
   unzipped folder.

`npm run build` produces the same thing in `dist/`, which can be loaded the same way without
unzipping anything.

### Firefox

1. Download `relego-extension-<version>-firefox.zip` from the [latest release][releases].
2. Open `about:debugging#/runtime/this-firefox`, choose **Load Temporary Add-on…**, and select the
   zip itself.

Firefox requires 128 or later, which is the first release with `optional_host_permissions`. A
temporary add-on is removed when Firefox restarts; a permanent install needs the package signed on
addons.mozilla.org, which is a release-process follow-up rather than a code change.

## Packaging

`npm run package` builds and zips both browsers, stamping the manifest of each with
`RelegoExtensionVersion` from `src/PackageVersions.props`:

| Package                                  | Background                        | Notes                                                                        |
|------------------------------------------|-----------------------------------|------------------------------------------------------------------------------|
| `relego-extension-<version>-chrome.zip`  | module service worker             | `vite.config.ts`                                                             |
| `relego-extension-<version>-firefox.zip` | classic event page (`background.scripts`) | `vite.config.firefox.ts`; adds `browser_specific_settings.gecko.id` for AMO   |

Firefox has no service worker in Manifest V3, so the Firefox background is rebuilt as a
self-contained IIFE with no ES module syntax and the manifest points at it through
`background.scripts`. Both packages carry the same popup and the same permissions. The Firefox
package signs under the add-on id `relego@relego.dev`, which identifies the listing on
addons.mozilla.org and must not change once published.

The toolbar and listing icons in `public/icons/` are the web app's favicon
(`src/relego.web/public/favicon.svg`) rendered at 16, 32, 48 and 128 px. `vite` copies `public/`
into `dist/`, so both packages ship them. If the mark changes, re-render these four files from the
new favicon — browsers reject a manifest that names an icon the package does not contain, and
Chrome does not read SVG icons.

[releases]: https://github.com/Krusty93/relego/releases

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
the server reports, which stays the source of truth for the selected region. Once that state is
usable, the popup shows only the confirmation with an **Edit configuration** control and the
**Open Relego** button: the address, one-time code and region fields appear again only when the user
asks for them, or when the connection cannot be used.

## Working in the popup

A toolbar popup closes as soon as the user clicks anywhere outside it, which gets in the way while
carrying the one-time code from Relego into the form. Two things cover that:

- The **open in a window** control in the popup header reopens the same form in a window of its own,
  so it stays put while the code is copied from Relego.
- The **Open Relego** button opens the Kindle Notebook Sync page of the Relego server
  (`<server address>/app/kindle-sync`) in a new tab, where the one-time code is generated. It is in
  both views: in the form it is enabled as soon as the address field holds a valid `http(s)` address,
  so the address can be set first and the page opened without leaving the popup to find the server;
  in the confirmation it opens the server the browser is paired with. Opening a tab needs no extra
  permission. The route lives in one constant, `KINDLE_SYNC_ROUTE` in `src/server-link.ts`.
- The address and the region are kept as they are entered, so reopening the popup comes back filled
  in. A kept address is not a pairing: the extension still reports "not connected" until a one-time
  code is accepted, and a pairing that already exists is never overwritten by what is typed.

See `specs/011-kindle-background-sync/` for the feature specification and contracts.
