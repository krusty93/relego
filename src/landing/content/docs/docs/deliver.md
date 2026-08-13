---
title: Deliver
description: Choose which highlights come back, then configure where Relego sends the recap.
stage: 2
sidebar:
  order: 2
subtitle: "A library of ten thousand highlights is a graveyard. The point of Relego is that a small, well-chosen handful comes back to you on a rhythm you can live with."
---

## How the choice is made

On each scheduled run the server picks a subset of your highlights using spaced
repetition: passages you have seen recently are unlikely to return, and
passages you have not seen in a long time move to the front. Your own weights
tilt that ordering.

Nothing about the selection leaves your server, and no model or service is
consulted. It is arithmetic over your own database.

## Schedule

Recaps go out daily at 18:00 in the server's local time zone unless you say
otherwise. Open **Settings** in the web interface, then use **When they go
out** to choose daily or weekly delivery and a time.

Weekly recaps go out on the same weekday you set them.

## How many highlights

Five per recap is the default. Anything from 1 to 15 works. Change it under
**When they go out** in **Settings**.

A larger number is not a better recap. Five is a comfortable e-ink page; fifteen
starts to feel like homework.

![Relego's Settings page, showing delivery addresses and recap schedule controls.](/images/docs/relego-web-settings.webp)

## Weights

Give a highlight a weight from 1 to 5 to change how often it comes back. Higher
means more often. Select the highlight in the web interface to adjust its
weight.

Use it sparingly. Weighting everything is the same as weighting nothing.

## Exclusions

Some highlights you never want to see again (a chapter heading the parser
picked up, a book you abandoned, or an author you have finished with). Exclude
them from the highlight, book, or author view in the web interface.

Exclusions are reversible and never delete anything. The highlight stays in your
library; it just stops being eligible.

## Fixing book titles

Kindle and Kobo metadata is often untidy (subtitles, edition numbers, publisher
noise). Rename a book in the web interface and every recap uses the new title.

## Choose a relay

Relego sends recaps over SMTP. It does not run a mail server.

:::caution
Personal Gmail and Outlook accounts no longer accept SMTP password
authentication. An app password on a personal account will not work as a general
solution, use a relay.
:::

Any of these have a free tier that comfortably covers one recap a day:

- [AWS SES](https://aws.amazon.com/ses/)
- [Resend](https://resend.com/docs/send-with-smtp)
- [MailerSend](https://www.mailersend.com/help/smtp-relay)
- [Mailgun](https://www.mailgun.com/features/smtp-server/)

Your own relay works too. Set the host, port, user, and password as
[environment variables](/docs/reference/environment/) on the server.

To test delivery locally before choosing a relay, use the
[smtp4dev demo profile](/docs/reference/environment/#a-working-example).

## Kindle: Send-to-Kindle

Amazon gives every Kindle an email address. Documents mailed to it appear on the
device. In **Settings**, add it under **Where recaps go**.

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
HTML. Add it under **Where recaps go** in **Settings**. Both channels can be
active at once:

```sh
relego config email inbox ""
```

**Kobo owners: use this channel.** Kobo has no Send-to-Kindle-style address, so
the inbox channel is Kobo's normal delivery route. Open the recap in your mail
app, or send it on to the reader with whatever method you already use.

For command-line automation, see
[CLI commands](/docs/reference/cli/#configuration-and-delivery).

## Next

Everything is wired. [Read your first recap →](/docs/revisit/)
