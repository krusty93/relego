---
title: Select and deliver
description: Choose which highlights come back, then configure where Relego sends the recap.
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

## Choose a relay

Relego sends recaps over SMTP. It does not run a mail server.

:::caution
Personal Gmail and Outlook accounts no longer accept SMTP password
authentication. An app password on a personal account will not work as a general
solution — use a relay.
:::

Any of these have a free tier that comfortably covers one recap a day:

- [AWS SES](https://aws.amazon.com/ses/)
- [Resend](https://resend.com/docs/send-with-smtp)
- [MailerSend](https://www.mailersend.com/help/smtp-relay)
- [Mailgun](https://www.mailgun.com/features/smtp-server/)

Your own relay works too. Set the host, port, user, and password as
[environment variables](/docs/reference/environment/) on the server.

## Try it without a relay first

Relego ships a demo profile with [smtp4dev](https://github.com/rnwood/smtp4dev),
a local mail catcher. Nothing is sent anywhere; you read the message in a web
UI.

```sh wrap
RELEGO_SMTP_HOST=smtp4dev RELEGO_SMTP_PORT=2525 docker compose --profile demo up -d
```

Recaps then land at `http://localhost:5000`, EPUB attachment and all. You can
download that attachment and forward it to your real Kindle address by hand to
confirm the document renders the way you expect.

## Kindle: Send-to-Kindle

Amazon gives every Kindle an email address. Documents mailed to it appear on the
device.

```sh
relego config email kindle "your-name@kindle.com"
```

:::danger
Amazon silently drops mail from unknown senders. Before you test delivery, add
the address your relay sends **from** to your Amazon *Approved Personal Document
E-mail List*, under Manage Your Content and Devices → Preferences → Personal
Document Settings.

If recaps never arrive and your server logs show a successful send, this is
almost always why.
:::

Kindle recaps arrive as an EPUB, so they open as a real document with a table of
contents rather than as an email.

## Inbox: the HTML channel

You can also have the recap delivered to any ordinary mailbox, formatted as
HTML:

```sh
relego config email inbox "you@example.com"
```

Both channels can be active at once. Clear the inbox channel with an empty
string:

```sh
relego config email inbox ""
```

**Kobo owners: use this channel.** Kobo has no Send-to-Kindle-style address, so
the inbox channel is how recaps reach you. Open the mail on your phone or
laptop, or send it on to the reader with whatever method you already use.

## Check your settings

```sh
relego config show
relego status
```

`status` also tells you when the next recap is due.

## Next

Everything is wired. [Read your first recap →](/docs/revisit/)
