---
title: Kindle browser extension
description: Install, connect, and manage Kindle Notebook sync with the Relego browser extension.
eyebrow: Reference
---

The Relego browser extension reads eligible highlights from your Kindle
Notebook in your own Amazon session and sends them to your Relego server. It
can import highlights made in Kindle apps as well as on a Kindle, without a USB
connection. Sync is optional; [local import](/docs/import/) remains available.

## Before you start

- Start your Relego server and open its web interface in the browser where you
  will install the extension. The browser must be able to reach that server.
- Use an Amazon account with Kindle books and highlights in one of the
  supported regions below.
- Keep the server, browser, and extension running for automatic sync. Browser
  alarms can run late and do not wake a sleeping computer.

The server has no authentication. Keep it on your own machine, trusted network,
or VPN; do not expose it to the public internet.

## Install

Download the extension package for your browser from the
[GitHub releases](https://github.com/Krusty93/relego/releases). Store listings
are not yet available.

### Chrome, Edge, and other Chromium browsers

1. Download `relego-extension-<version>-chrome.zip` and extract it to a folder
   you intend to keep.
2. Open `chrome://extensions` (or your browser's extensions page), enable
   **Developer mode**, and choose **Load unpacked**.
3. Select the extracted folder.

### Firefox

Firefox 128 or later is required.

1. Download `relego-extension-<version>-firefox.zip`.
2. Open `about:debugging#/runtime/this-firefox`, choose **Load Temporary
   Add-on**, and select the ZIP file.

Firefox removes temporary add-ons when it restarts. Load the ZIP again after
restarting; a permanent install requires a signed add-on.

## Connect

1. In the Relego web interface, open **Sync** and select **Connect Kindle cloud**.
2. Open the Relego extension from the browser toolbar. Enter the **Server URL**
   and **One-time code** shown on the Sync page, then choose your Amazon region
   and save. You can open the popup in a separate window to keep it visible
   while copying the values.
3. Approve the browser's requested access to your Relego server and the selected
   Kindle Notebook site. Sign in directly on Amazon's site in the same browser
   if needed.
4. Return to **Sync** to follow the first import and its result. If the code is
   no longer visible, select **Generate a new code**; the old code stops working.

For a server on another machine, use an address reachable from the browser,
not `localhost`, which refers to the browser's own machine.

## Supported regions

Choose the marketplace for your Kindle library in the extension. Relego shows
the active region on the Sync page; change it using **Edit configuration** in
the extension. Only these validated Kindle Notebook hosts are available:

| Region | Kindle Notebook host |
| --- | --- |
| United States | `read.amazon.com` |
| Canada | `read.amazon.ca` |
| United Kingdom | `read.amazon.co.uk` |
| Germany | `lesen.amazon.de` |
| France | `lire.amazon.fr` |
| Italy | `leggi.amazon.it` |
| Spain | `leer.amazon.es` |
| Brazil | `ler.amazon.com.br` |

Changing regions does not require pairing again or remove existing highlights.
The next sync re-reads the new region, where you may need to sign in again.
Other regions, including Japan, China, Denmark, Ireland, and Poland, are not
currently supported.

## Sync and recovery

The extension starts routine syncs while the browser is running. The default is
once an hour; change **Sync frequency** on the Relego Sync page to 15, 30, or
45 minutes, or 1, 2, 4, 6, 12, 18, or 24 hours. The extension applies a new
frequency at its next check-in, and the page shows when the next run is due.
The extension checks in every 60 seconds independently of the sync frequency.
After the browser returns from being closed or asleep, at most one catch-up
sync runs; missed runs do not accumulate.

Use **Sync now** to check for new highlights or **Full resync** to re-read all
available books. Requests wait for the browser's next check-in; repeated
imports and full resyncs do not duplicate previously imported highlights.

- **Amazon sign-in needed:** Sign in again on the selected region's Amazon
  site in the browser with the extension, then retry sync.
- **Your browser has not checked in:** Open the browser and make sure the
  extension is installed and enabled and the server is reachable. On Firefox,
  reload the temporary add-on after a restart.
- **Last sync did not finish:** Follow the error and recovery steps on the Sync
  page, then retry. Amazon can change the Notebook layout, which may require
  an extension update.

After 45 days without newly synchronized Kindle cloud content, Relego displays
a reminder and can email one when mail delivery is configured. **Disconnect**
on the Sync page revokes the connection but keeps previously imported highlights.

## Coverage and privacy

Kindle Notebook sync covers eligible Amazon-synced highlights. Personal and
sideloaded documents may be absent, and publisher export limits can truncate
highlights. Use [local Kindle import](/docs/import/#kindle) for content that is
missing or unsupported; cloud sync and local import can be used together.

Sign in only on Amazon's own pages. Relego does not ask for your Amazon
password, and the extension does not read or export Amazon cookies. Notebook
page HTML stays in the browser; parsed highlights are sent to your server.
The extension uses browser storage and alarms and asks for host access at
runtime only for your server and selected Kindle Notebook site.