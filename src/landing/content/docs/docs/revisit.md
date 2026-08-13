---
title: Revisit
description: Trigger your first recap, see what one looks like, and close the loop.
stage: 4
sidebar:
  order: 4
---

The last stage is the only one that matters: the passage arrives, and you read
it again.

## Do not wait for the schedule

```sh
relego recap trigger
```

The server selects, renders, and sends immediately. Everything else stays
unchanged — this does not shift the next scheduled recap.

## What arrives

An EPUB with one entry per highlight: the passage, the book, the author. On a
Kindle it opens as a document in your library, so you can page through it,
highlight *inside* it, and leave it half-read.

> #### Relego Daily Recap (2026-05-21 18:00)
>
> - _"Care About Your Craft"_
>   — **The Pragmatic Programmer** by David Thomas & Andrew Hunt
>
> - _"Clean code is simple and direct."_
>   — **Clean Code** by Robert C. Martin
>
> - _"In a hole in the ground there lived a hobbit."_
>   — **The Hobbit** by J.R.R. Tolkien
>
> - _"The only way to do great work is to love what you do."_
>   — **Steve Jobs** by Walter Isaacson
>
> - _"Violence is the last refuge of the incompetent."_
>   — **Foundation** by Isaac Asimov

The inbox channel sends the same content as HTML.

## If nothing arrives

| What you see | What to do |
| --- | --- |
| `No eligible highlights available` | The library is empty, or everything is excluded. Run [Import](/docs/import/), then check `relego exclude list` |
| The command errors | Confirm the server is up with `docker compose ps` |
| The send succeeds, no mail on the Kindle | The sending address is not on your Amazon approved list — see [Select and deliver](/docs/select/) |
| Nothing in smtp4dev | The `demo` profile is not running, or the server is not pointed at `smtp4dev:2525` |

More cases in [Troubleshooting](/docs/reference/troubleshooting/).

## Living with it

Once the first recap lands, Relego needs almost nothing from you. Plug the
reader in every few weeks and run `relego import` to pick up what you have
highlighted since. Everything else happens on schedule.

When a passage comes back that you are tired of, exclude it. When one comes back
that stops you, weight it up. The library gets better the longer you use it.

## The loop closes

The passage you just re-read is back in the rotation, its clock reset. The next
book you underline joins the same circuit at [Capture](/docs/capture/).
