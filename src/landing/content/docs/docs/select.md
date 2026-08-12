---
title: Select
description: How Relego chooses which highlights come back, and how to steer it with schedules, weights, and exclusions.
stage: 3
sidebar:
  order: 3
---

A library of ten thousand highlights is a graveyard. The point of Relego is that
a small, well-chosen handful comes back to you on a rhythm you can live with.

## How the choice is made

On each scheduled run the server picks a subset of your highlights using spaced
repetition: passages you have seen recently are unlikely to return, and
passages you have not seen in a long time move to the front. Your own weights
tilt that ordering.

Nothing about the selection leaves your server, and no model or service is
consulted. It is arithmetic over your own database.

## Schedule

Recaps go out daily at 18:00 in the server's local time zone unless you say
otherwise.

```sh
relego config schedule daily 07:30
relego config schedule weekly 09:00
relego config schedule show
```

Weekly recaps go out on the same weekday you set them.

## How many highlights

Five per recap by default. Anything from 1 to 15 works.

```sh
relego config count 8
relego config count show
```

A larger number is not a better recap. Five is a comfortable e-ink page; fifteen
starts to feel like homework.

## Weights

Give a highlight a weight from 1 to 5 to change how often it comes back. Higher
means more often.

```sh
relego weight set 42 5
relego weight list
```

Use it sparingly. Weighting everything is the same as weighting nothing.

## Exclusions

Some highlights you never want to see again — a chapter heading the parser
picked up, a book you abandoned, an author you have finished with. Exclude at
whichever level makes sense:

```sh
relego exclude add highlight 128
relego exclude add book 17
relego exclude add author 4
relego exclude list
relego exclude remove book 17
```

Exclusions are reversible and never delete anything. The highlight stays in your
library; it just stops being eligible.

## Fixing book titles

Kindle and Kobo metadata is often untidy — subtitles, edition numbers, publisher
noise. Rename a book once and every recap uses the new title:

```sh
relego rename-book 17 "The Pragmatic Programmer"
```

## Next

Something has been chosen. [Get it to your reader →](/docs/deliver/)
