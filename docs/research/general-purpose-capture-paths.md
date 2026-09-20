# General-Purpose Capture Paths Research

**Date:** 2026-09-20  
**Status:** Wayfinder evidence brief  
**Decision ticket:** [Determine viable general-purpose capture paths](https://github.com/Krusty93/relego/issues/448)

## Question

Which practical ingestion paths can Relego support for WhatsApp, Telegram, arbitrary web pages, email, CSV, and Markdown without entering a first-party connector-breadth race?

## Decision

Relego should build around a **small canonical ingestion contract**, then divide capture paths into three tiers:

1. **Core first-party formats:** CSV and Markdown imports, followed by RFC 5322 `.eml` import. They are portable, historical, local, and independent of platform APIs.
2. **First-party capture clients over the same contract:** a narrowly permissioned browser extension for explicit page/selection capture. A Telegram bot that receives forwarded messages is a plausible reference adapter, but not a history importer.
3. **Community plugins:** Telegram user-history access through TDLib, WhatsApp Business forwarding, WhatsApp text-export parsing, and other platform-specific bridges whose authentication, policy, or operational burden should not enter Relego core.

This strategy provides useful general-purpose entry points while keeping proprietary platform behavior outside the core domain.

## Confidence labels

- **[verified]** Directly supported by the cited source.
- **[inference]** Reasoned from verified facts.
- **[unknown]** Not established by the reviewed sources.
- **[recommendation]** Product decision proposed from the evidence.

## Decision matrix

| Path | Automation | Personal history | Authentication | Operations and risk | Proposed ownership |
| --- | --- | --- | --- | --- | --- |
| CSV | File upload/watch | Complete file contents | None | Low; schema/versioning must be explicit | Core first-party |
| Markdown | File upload/watch | Complete file contents | None | Low; front matter or a documented convention is needed for fidelity | Core first-party |
| RFC 5322 `.eml` | File upload or forwarding gateway | Any messages the user exports/forwards | None for file import | Low for file import; public inbound SMTP is materially harder | Core parser; gateway later/plugin |
| Browser extension | User-triggered capture | Current page/selection only | Browser permission and Relego endpoint credential | Medium; store review and page extraction maintenance | Narrow first-party client |
| Telegram Bot API | Automatic after messages are sent/forwarded to the bot | No arbitrary pre-existing personal chats | Scoped bot token | Low-medium; webhook or long polling | Reference adapter/plugin |
| Telegram TDLib | Automatic full-client access | Yes | User phone/code/2FA and persistent session | High security and binary/runtime burden | Community plugin |
| WhatsApp Cloud API | Automatic for a registered business endpoint | No personal account history | Meta app, WABA, business phone, tokens | High provisioning and policy burden | Community plugin |
| WhatsApp consumer export | Manual per chat | Exported chat only | None | Format and current limits need verification | Community file plugin |

## Evidence by path

### CSV and Markdown

CSV and Markdown are local text formats with no platform account, API quota, or external runtime dependency. CSV has an informational common format in RFC 4180; Markdown can target CommonMark while using documented front matter for Relego-specific metadata. **[verified]**

They support both historical migration and incremental import if Relego records source identity and import checkpoints. Deduplication, provenance, and schema evolution are Relego responsibilities rather than platform risks. **[inference]**

**[recommendation]** Define one canonical import contract first, then provide:

- a versioned CSV schema suitable for Readwise/PastReads migration and generic tabular import;
- a versioned Markdown convention preserving book, author, chapter, annotation type, locator, source ID, timestamps, tags, notes, and optional question/answer fields;
- clear round-trip guarantees where Relego exports its own formats.

### Email

SMTP and Internet Message Format are open standards. RFC 5322 messages carry useful provenance such as `From`, `Date`, `Message-ID`, and `Subject`. **[verified]** MIME is required for realistic non-ASCII and multipart content.

An inbound email address is not operationally free for a self-hoster: reliable public receipt generally requires a domain, MX/DNS configuration, a reachable and secured receiver or provider, abuse controls, and delivery parsing. **[inference]** Relego's existing outbound SMTP support does not remove those requirements.

**[recommendation]** Put RFC 5322 parsing in core and initially support `.eml` upload/import. Treat a managed forwarding address or self-hosted SMTP receiver as a later adapter. This preserves the open protocol without making mail-server operation part of onboarding.

### Browser extension

Chrome's `activeTab` permission grants temporary current-tab access after a user gesture. The `scripting` API can then extract a selected passage and page metadata. Narrow permissions reduce warning and policy exposure compared with persistent `<all_urls>` access. **[verified]**

This path captures new material the user is viewing; it does not import historical browsing. The minimum useful payload is selected text, page title, URL, author when detectable, capture timestamp, and optional user note/tags. **[recommendation]**

The extension should send the canonical payload to a user-configured Relego endpoint and should not implement site-specific parsers in core. Site-specific enrichment belongs in optional extractors/plugins. **[recommendation]**

Firefox compatibility is plausible through WebExtensions but was not independently verified in this research pass. **[unknown]**

### Telegram

The Telegram Bot API receives updates through long polling or webhooks and exposes structured message fields including chat, sender, date, text, entities, and forwarding origin. Unconsumed updates are retained for a limited period, and a bot cannot read arbitrary pre-existing private chat history. **[verified]**

A bot is therefore useful only when the user deliberately sends or forwards material to it. That is a capture workflow, not account synchronization. **[recommendation]** It can be a small reference adapter after the canonical HTTP contract is stable.

TDLib is a full Telegram client. It requires an application ID/hash and the user's complete authorization flow, can persist a user session, and exposes `getChatHistory`. **[verified]** It can import real history but substantially increases secret handling, native dependency, and support burden.

**[recommendation]** Keep TDLib history ingestion in a community plugin. Core should not hold a user's full Telegram session.

### WhatsApp

Meta's official WhatsApp Cloud API is part of the WhatsApp Business Platform. It requires a Business Portfolio, WhatsApp Business Account, registered business phone number, access tokens, and policy-compliant messaging/webhook setup. **[verified]** It is not a personal-account history API.

This API can support a workflow where a user forwards material to a Relego-operated business number, but it cannot provide general personal WhatsApp history synchronization. **[inference]** The provisioning and policy burden also conflicts with low-friction self-hosting.

The current official limits and format of WhatsApp's consumer chat-export feature were not verified because the reviewed Help Center URL was unavailable. **[unknown]** A text-export parser may be feasible, but it should remain a community plugin until the format and support policy are established.

## Canonical ingestion implications

Every adapter should map into a common envelope before source-specific data reaches the core domain:

| Field | Purpose |
| --- | --- |
| `sourceType` and `sourceItemId` | Stable provenance and deduplication |
| `contentType` | Highlight, note, bookmark, excerpt, question, answer, or summary |
| `text` | Captured content |
| `title`, `author`, `chapter` | Reading context when available |
| `url` or `locator` | Return path to the source |
| `sourceCreatedAt`, `capturedAt` | Original and ingestion chronology |
| `sourceActor` | Sender/author where meaningful |
| `tags` and `note` | User organization and interpretation |
| `rawMetadata` | Loss-minimizing extension data without polluting core concepts |

The product decision about exact concepts and identity remains with [Define Relego's canonical reading-content model](https://github.com/Krusty93/relego/issues/449). This research establishes the range of source fidelity that model must accommodate.

## Prioritized product path

1. **[recommendation]** Ship versioned CSV and Markdown imports against the canonical ingestion contract.
2. **[recommendation]** Add `.eml` import if real users show an email-capture job; avoid requiring inbound SMTP during self-hosted onboarding.
3. **[recommendation]** Build a minimal browser extension using explicit user gestures and narrow permissions after the ingest endpoint is stable.
4. **[recommendation]** Publish a Telegram bot reference adapter for forward-to-Relego capture if demand exists.
5. **[recommendation]** Document plugin boundaries for TDLib and WhatsApp bridges; do not place their credentials or platform policy burden in core.

## Unknowns and falsification tests

| Unknown | Test |
| --- | --- |
| Exact WhatsApp consumer export format and current limits | Locate current official Help Center documentation and test exports on iOS/Android before accepting a plugin contract |
| Firefox permission parity and store review behavior | Build and submit the smallest cross-browser extension prototype |
| Whether users will forward messages to a Telegram bot | Run a concierge prototype before maintaining a first-party adapter |
| Whether `.eml` import solves a real job | Interview and test with users who currently email notes to themselves |
| Whether one canonical contract preserves source-specific fidelity | Round-trip representative fixtures from every accepted source before freezing version 1 |

The tiering should change if a platform publishes a supported personal-history API with scoped authorization and stable data contracts.

## Sources

All sources accessed 2026-09-20.

- Meta, WhatsApp Cloud API Overview: <https://developers.facebook.com/docs/whatsapp/cloud-api/overview>
- Telegram Bot API: <https://core.telegram.org/bots/api>
- Telegram TDLib Getting Started: <https://core.telegram.org/tdlib/getting-started>
- Chrome Extensions permission list: <https://developer.chrome.com/docs/extensions/reference/permissions-list>
- Chrome Web Store Program Policies: <https://developer.chrome.com/docs/webstore/program-policies>
- RFC 5321, Simple Mail Transfer Protocol: <https://www.rfc-editor.org/rfc/rfc5321>
- RFC 5322, Internet Message Format: <https://www.rfc-editor.org/rfc/rfc5322>
- RFC 4180, Common Format and MIME Type for CSV Files: <https://www.rfc-editor.org/rfc/rfc4180>
- CommonMark Specification: <https://spec.commonmark.org/>