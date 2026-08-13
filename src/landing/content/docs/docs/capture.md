---
title: Capture
description: What Relego reads off your Kindle or Kobo, and how highlights get there in the first place.
stage: 1
sidebar:
  order: 1
---

The trip starts on the device, before Relego is involved at all.

## Kindle

When you underline a passage on a Kindle, the device appends it to a plain text
file at `documents/My Clippings.txt` in its internal storage. Every highlight,
note, and bookmark you have ever made is in there, in the order you made it.

A single entry looks like this:

```text
The Pragmatic Programmer (David Thomas & Andrew Hunt)
- Your Highlight on page 12 | Location 210-211 | Added on Sunday, 3 May 2026 09:14:02

Care About Your Craft
==========
```

Relego reads the quote, the book title, and the author. Bookmarks and empty
entries are skipped.

:::note
`My Clippings.txt` only contains highlights made on the device itself.
Highlights you made in the Kindle mobile or desktop apps live in Amazon's cloud
and are not in this file.
:::

## Kobo

Kobo stores highlights in a SQLite database at `.kobo/KoboReader.sqlite` on the
device. `.kobo` is a hidden folder, so you may need to enable hidden files in
your file manager to see it.

Relego reads the bookmark table and pairs each highlight with its book title and
author. You do not need any SQLite tooling installed, the CLI reads the file
directly.

## What Relego needs from you

Nothing yet. Just make sure you have highlights to import:

| Device | File | Notes |
| --- | --- | --- |
| Kindle | `documents/My Clippings.txt` | Plain text, always present once you have highlighted something |
| Kobo | `.kobo/KoboReader.sqlite` | Hidden folder; copy it off the device if your OS blocks direct reads |

Both files are read-only as far as Relego is concerned. Nothing on your device
is modified, and nothing is uploaded anywhere.

If you own both a Kindle and a Kobo, you can use both. Relego imports each
source in the same run and reports a failure on one without abandoning the
other.

## Next

The highlights need somewhere to land.
[Start the server and import them →](/docs/import/)
