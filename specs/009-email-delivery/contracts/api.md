# API Contracts: Email Delivery

**Feature**: 009-email-delivery
**Date**: 2026-06-07

---

## Modified Endpoints

### GET /settings

**Change**: Response now includes `deliveryEmail` field.

**Response** (200 OK):

```json
{
  "schedule": "weekly",
  "deliveryDay": "sunday",
  "deliveryTime": "18:00",
  "count": 5,
  "kindleEmail": "user_abc123@kindle.com",
  "deliveryEmail": "user@example.com",
  "timezone": "Europe/Rome"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `deliveryEmail` | `string \| null` | Regular email for HTML recap delivery. `null` when not configured. |

---

### PATCH /settings

**Change**: Request body now accepts `deliveryEmail` field. Response includes `deliveryEmail`.

**Request** (application/json):

```json
{
  "schedule": "daily",
  "deliveryTime": "09:00",
  "count": 10,
  "kindleEmail": "user_abc123@kindle.com",
  "deliveryEmail": "user@example.com",
  "timezone": "America/New_York"
}
```

All fields are optional — only provided fields are changed.

| Field | Type | Validation |
|-------|------|------------|
| `deliveryEmail` | `string \| null` | If provided and non-empty: must be valid email format. Empty string `""` clears the field (sets to null). `null`/absent = no change. |

**Validation errors** (422 Unprocessable Entity):

```json
{
  "errors": {
    "deliveryEmail": ["Invalid email format."]
  }
}
```

**Response** (200 OK): Same as `GET /settings` — returns full settings including both email fields.

---

### POST /settings/test-email

**Change**: Accepts optional request body with `channel` parameter. Response format extended for multi-channel results.

**Request** (application/json, optional):

```json
{
  "channel": "delivery"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `channel` | `string` | No | `"kindle"`, `"delivery"`, or `"both"`. Default (null/absent): auto-detect — sends to all configured channels. |

**Validation errors** (422):

- `"channel"` is not one of `"kindle"`, `"delivery"`, `"both"`, or null.
- Specified channel's email is not configured: `{"errors": {"deliveryEmail": ["Delivery email must be configured before sending a test email."]}}`

**Response — single channel success** (200 OK):

```json
{
  "message": "Test email sent successfully to delivery@example.com."
}
```

**Response — both channels success** (200 OK):

```json
{
  "results": {
    "kindle": { "success": true },
    "delivery": { "success": true }
  }
}
```

**Response — partial failure** (200 OK): One channel succeeded, one failed:

```json
{
  "results": {
    "kindle": { "success": true },
    "delivery": { "success": false, "error": "Connection refused (127.0.0.1:25)" }
  }
}
```

**Response — complete failure** (502 Bad Gateway):

```json
{
  "title": "SMTP delivery failed.",
  "detail": "Connection refused (127.0.0.1:25)",
  "status": 502
}
```

---

### GET /status

**Change**: Response now includes `deliveryEmailConfigured` field.

**Response** (200 OK):

```json
{
  "totalHighlights": 42,
  "totalBooks": 7,
  "totalAuthors": 5,
  "excludedHighlights": 3,
  "excludedBooks": 1,
  "excludedAuthors": 0,
  "nextRecap": "2026-06-08T18:00:00.0000000+00:00",
  "lastRecapStatus": "delivered",
  "lastRecapError": null,
  "kindleEmailConfigured": true,
  "deliveryEmailConfigured": true
}
```

| Field | Type | Description |
|-------|------|-------------|
| `deliveryEmailConfigured` | `boolean` | `true` if `delivery_email` is a non-empty string. |

---

## New Contracts (C#)

### TestEmailRequest

```csharp
namespace Relego.Core.Contracts;

/// <summary>
/// Optional request body for POST /settings/test-email.
/// </summary>
public sealed record TestEmailRequest
{
    /// <summary>
    /// Channel to test: "kindle", "delivery", or "both".
    /// null = auto-detect (test all configured channels).
    /// </summary>
    public string? Channel { get; set; }
}
```

This contract lives in `Relego.Core/Contracts/` alongside other shared contracts.
