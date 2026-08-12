---
title: Deliver
description: Set up an SMTP relay, Send-to-Kindle, and the inbox channel so recaps actually arrive.
stage: 4
sidebar:
  order: 4
---

This is the stage that breaks most often, because it depends on two things
outside Relego: a mail relay that will send for you, and Amazon's approved
sender list.

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
