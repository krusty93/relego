---
title: Kindle browser extension
description: Install, connect, and manage Kindle Notebook sync with the Relego browser extension.
eyebrow: Reference
---

The Relego browser extension reads eligible highlights from your Kindle
Notebook in your own Amazon session and sends them to your Relego server. It
can import highlights made in Kindle apps as well as on a Kindle, without a USB
connection.

## Install

Install the extension from the store for your browser:

- [Relego on the Chrome Web Store](https://chromewebstore.google.com/detail/relego/nlpcdgfkcoiiighmhemlpkjkcnoojdhp)
  for Chrome, Edge, and other Chromium browsers.
- [Relego on Firefox Add-ons](https://addons.mozilla.org/en-US/firefox/addon/relego/)
  for Firefox 140 or later.

:::note

For a development build instead, download the
extension from
[GitHub releases](https://github.com/krusty93/relego/releases), and:

- **Chrome / Chromium**: extract it, open `chrome://extensions`, enable **Developer mode**, choose **Load unpacked**, and
select the extracted folder.
- **Firefox**: open `about:debugging#/runtime/this-firefox`, choose **Load Temporary Add-on**, and
select the ZIP file. Firefox removes temporary add-ons when it restarts;
install from the store listing for a permanent install.

:::

## Connect

1. In the Relego web interface, open **Sync** and select **Connect Kindle cloud**.
2. Open the Relego extension from the browser toolbar. Enter the **Server URL**
   and **One-time code** generated on the Sync page, then choose your Amazon region
   and save. You can open the popup in a separate window to keep it visible
   while copying the values.
3. Approve the browser's requested access to your Relego server and the selected
   Kindle Notebook site. Sign in directly on Amazon's site in the same browser
   if needed.
4. Return to **Sync** to follow the first import and its result. If the code is
   no longer visible, select **Generate a new code**; the old code stops working.

## Supported regions

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

## Sync

The extension starts routine syncs while the browser is running. The default is
once an hour; change **Sync frequency** on the Relego Sync page to 15, 30, or
45 minutes, or 1, 2, 4, 6, 12, 18, or 24 hours. The extension applies a new
frequency at its next check-in, and the page shows when the next run is due.
The extension checks in every 60 seconds independently of the sync frequency.
After the browser returns from being closed or asleep, at most one catch-up
sync runs and missed runs do not accumulate.

Use **Sync now** to check for new highlights or **Full resync** to re-read all
available books. Requests wait for the browser's next check-in. Repeated
imports and full resyncs do not duplicate previously imported highlights.

**Disconnect** on the Sync page revokes the connection but keeps previously imported highlights.

After 45 days without newly synchronized Kindle cloud content, Relego displays
a reminder and can email one when mail delivery is configured.

## Troubleshooting

- **Amazon sign-in needed:** Sign in again on the selected region's Amazon
  site in the browser with the extension, then retry sync.
- **Your browser has not checked in:** Open the browser and make sure the
  extension is installed and enabled and the server is reachable. On Firefox,
  reload a temporary development add-on after a restart.
- **Last sync did not finish:** Follow the error and recovery steps on the Sync
  page, then retry. Amazon can change the Notebook layout, which may require
  an extension update.
- **Still need help?:** Open the [a ticket on GitHub](https://github.com/krusty93/relego/issues/new/choose) for further assistance.

## Coverage and privacy

Kindle Notebook sync covers eligible Amazon-synced highlights. Personal and
sideloaded documents may be absent, and publisher export limits can truncate
highlights. Use [local Kindle import](/docs/import/#kindle) for content that is
missing or unsupported. <u>Cloud sync and local import can be used together.</u>

Sign in only on Amazon's own pages. Relego does not ask for your Amazon
password, and the extension does not read or export Amazon cookies.
The extension uses browser storage and alarms and asks for host access at
runtime only for your server and selected Kindle Notebook site.
