# Quickstart: Email Delivery

**Feature**: 009-email-delivery
**Date**: 2026-06-07

---

## Prerequisites

- .NET 10 SDK for building and running tests

---

## 1. Start the Development Environment

**Terminal 1 — smtp4dev (SMTP relay)**

```bash
docker run --rm -it -p 5000:80 -p 2525:2525 rnwood/smtp4dev:latest
```

- Web UI: http://localhost:5000
- SMTP port: 2525

**Terminal 2 — Server**

```bash
dotnet run --project src/Relego.Server/Relego.Server.csproj
```

The server reads SMTP configuration from `appsettings.Development.json` (host `localhost`, port `2525`, no auth, `DevMailDeliveryService`). Server listens on http://localhost:8080.

Verify both are running:
```bash
curl http://localhost:8080/status
```

---

## 2. Configure Delivery Email

### Via HTTP API

```bash
# Set delivery email
curl -X PATCH http://localhost:8080/settings \
  -H "Content-Type: application/json" \
  -d '{"deliveryEmail": "test@relego.local"}'

# Verify
curl http://localhost:8080/settings | jq .
```

### Via CLI

```bash
# Set a regular delivery email (smtp4dev will capture it)
relego config delivery-email test@relego.local

# Verify it's set
relego config show
```

---

## 3. Import Highlights (if none exist)

```bash
relego import --file docs/examples/kindle-highlights.txt
```

Or via API:

```bash
curl -X POST http://localhost:8080/highlights/import \
  -H "Content-Type: application/json" \
  -d '{"books":[{"title":"Book Title","author":"Author Name","highlights":[{"text":"Your highlight here..."}]}]}'
```

---

## 4. Trigger a Recap

```bash
# Trigger a recap immediately
curl -X POST http://localhost:8080/recaps
```

Response:
```json
{"status": "triggered", "scheduledFor": "2026-06-07T..."}
```

---

## 5. Verify Email Delivery

### Check smtp4dev Web UI

Open http://localhost:5000 and verify:

1. **Kindle channel** (if `kindle_email` is set): An email with subject "Your Relego Recap" containing an EPUB attachment (`.epub` file).
2. **Email channel** (if `delivery_email` is set): An email with subject "Your Relego Recap" containing HTML body with highlights inline.

### Inspect Raw Email Source

In smtp4dev, click on the email → "View Source" tab. Verify:
- MIME structure: `Content-Type: multipart/alternative`
- Parts: `text/plain` + `text/html`
- HTML part contains inline styles, book titles, highlight text
- Plain-text part contains text-only formatted highlights

### Inspect HTML Rendering

In smtp4dev, click on the email → "HTML" tab to see the rendered HTML. Verify:

- Branded header with "Relego" logotype
- Recap date displayed prominently
- Highlights grouped by book with title, author, and highlight text
- Footer with "Sent by Relego" and project link
- Responsive layout adapts on narrow viewport

---

## 6. Test Email Endpoint

```bash
# Test delivery channel
curl -X POST http://localhost:8080/settings/test-email \
  -H "Content-Type: application/json" \
  -d '{"channel": "delivery"}'

# Test Kindle channel
curl -X POST http://localhost:8080/settings/test-email \
  -H "Content-Type: application/json" \
  -d '{"channel": "kindle"}'

# Test both (or omit body for auto-detect)
curl -X POST http://localhost:8080/settings/test-email \
  -H "Content-Type: application/json" \
  -d '{"channel": "both"}'
```

Check smtp4dev for the test emails (subject: "Relego - Test Email", plain-text body).

---

## 7. Run Tests

```bash
# Run all tests
dotnet test src/Relego.slnx

# Run email-delivery specific tests
dotnet test src/Relego.slnx --filter "FullyQualifiedName~HtmlEmailComposer"
dotnet test src/Relego.slnx --filter "FullyQualifiedName~RecapServiceDualChannel"
dotnet test src/Relego.slnx --filter "FullyQualifiedName~SettingsDeliveryEmail"
dotnet test src/Relego.slnx --filter "FullyQualifiedName~ConfigDeliveryEmail"
```

---

## 8. Verify Edge Cases

### Neither Email Configured

```bash
# Clear both emails
curl -X PATCH http://localhost:8080/settings \
  -H "Content-Type: application/json" \
  -d '{"kindleEmail": "", "deliveryEmail": ""}'

# Trigger recap — server console shows warning: "No delivery channels configured"
curl -X POST http://localhost:8080/recaps
```

### SMTP Failure Isolation

1. Stop smtp4dev: `Ctrl+C` in the smtp4dev terminal (or `docker stop` the container)
2. Configure both emails and trigger a recap
3. Both channels should fail independently — check server console for per-channel error logs
4. Restart smtp4dev: re-run the `docker run` command from step 1
5. Set only one email, trigger recap — other channel should still work

---

## 9. Real Email Client Verification (Optional)

smtp4dev shows HTML rendering locally. For final verification in real email clients, configure a real SMTP provider by editing `src/Relego.Server/appsettings.Development.json`:

1. Set `Smtp.Host`, `Smtp.Port`, `Smtp.Username`, `Smtp.Password` to your real SMTP provider
2. Set `delivery_email` to your real email address
3. Trigger a recap
4. Check rendering in Gmail, Apple Mail, and Outlook

Verify: responsive layout, no horizontal scrolling on mobile, readable font sizes, correct brand colors, highlight grouping by book.
