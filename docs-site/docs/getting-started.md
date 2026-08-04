---
sidebar_position: 2
---

# Getting started

This guide takes you from zero to your first Relego recap.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and Docker Compose

## 1. Clone the repository

```sh
git clone https://github.com/krusty93/relego.git
cd relego
```

## 2. Start the server and web UI

```sh
docker compose --profile app up -d
```

Open **http://localhost:8081** in your browser. You should see the Relego web UI.

:::tip Try it without a mail server first
Use the `demo` profile to spin up a local fake SMTP server (smtp4dev). No real email is sent — you can view captured messages at http://localhost:5000.

```sh
docker compose --profile demo up -d
```
:::

## 3. Import your highlights

In the web UI, go to **Import** and drag your `My Clippings.txt` (Kindle) or `KoboReader.sqlite` (Kobo) file onto the drop zone.

Alternatively, use the CLI:

```sh
# Docker
docker compose run --rm relego-cli import "/path/to/My Clippings.txt"
```

Sample files are included in `docs/examples/` if you don't have a device handy.

## 4. Set up delivery

Go to **Settings** in the web UI:

1. **Delivery** — enter your Kindle email address (e.g. `yourname@kindle.com`)
2. **Email server (SMTP)** — enter your SMTP server details

:::important
Amazon Send-to-Kindle only accepts emails from approved senders. Add your sender address to your Amazon **Approved Personal Document E-mail List** before testing delivery: [Amazon content settings →](https://www.amazon.com/hz/mycd/myx#/home/settings/pdoc)
:::

:::tip SMTP providers
Gmail and Outlook personal accounts no longer support SMTP with password authentication. Use a free SMTP relay instead — [Resend](https://resend.com/docs/send-with-smtp), [MailerSend](https://www.mailersend.com/help/smtp-relay), [AWS SES](https://aws.amazon.com/ses/), or [Mailgun](https://www.mailgun.com/features/smtp-server/) all offer free tiers suitable for Relego's usage.
:::

## 5. Send a test recap

In the web UI, go to **Settings → Email server** and click **Test connection** to verify your SMTP settings.

Then go to **Recaps** and click **Send recap now** to trigger your first delivery without waiting for the schedule.

That's it — recaps will arrive on the configured schedule (default: daily at 18:00 in the server's local timezone).
