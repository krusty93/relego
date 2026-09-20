# Kindle Web Auto-Sync Research

**Date:** 2026-09-20  
**Status:** Wayfinder evidence brief  
**Decision ticket:** [Determine a viable automatic Kindle web sync path](https://github.com/Krusty93/relego/issues/447)

## Question

Which technically and legally viable mechanism can import Kindle highlights without requiring the user to copy `My Clippings.txt`, and can that mechanism support reliable unattended synchronization?

## Decision

Relego should **not promise unattended Kindle web synchronization**.

Amazon exposes no documented public API for a reader's Kindle highlights. The practical web-based mechanisms all read the undocumented `read.amazon.com/notebook` surface through an authenticated browser session. That session expires, the page contract can change, and Amazon's Conditions of Use restrict automated extraction and agent behavior.

For founder dogfooding, Relego should test an **optional, user-initiated browser-assisted import**:

1. The user signs into Amazon normally in their browser.
2. A narrowly scoped extension or bookmarklet reads the currently open Kindle Notebook page.
3. It sends structured annotations to the user's Relego server.
4. It never receives or stores Amazon credentials and never attempts CAPTCHA or MFA automation.

This removes manual file transfer while keeping a human in the loop. It must be described as **browser-assisted import**, not auto-sync. `My Clippings.txt` and local-device import remain the reliable supported fallback.

## Confidence labels

- **[verified]** Directly supported by the cited source.
- **[inference]** Reasoned from verified facts.
- **[unknown]** Not established by the reviewed sources.
- **[recommendation]** Product decision proposed from the evidence.

## Mechanism comparison

| Mechanism | Automation | Credentials | Coverage and metadata | Reliability | Policy risk | Recommendation |
| --- | --- | --- | --- | --- | --- | --- |
| Public Kindle highlights API | None | N/A | No documented API was found; Readwise states Kindle offers no highlights API | N/A | N/A | Do not plan around one |
| `My Clippings.txt` / connected device | User-triggered local import | None | Store and sideloaded content; title, author where present, text, location, timestamp; no chapter field | Highest of reviewed paths | Low | Keep as supported fallback |
| Browser extension on Kindle Notebook | User signs in; extraction can run in the authenticated browser | Extension need not see the password | Store-bought books represented in cloud notebook; location rather than page; export limits apply | Medium: sessions expire and DOM behavior is undocumented | Material | Prototype as an explicit user action |
| Bookmarklet on Kindle Notebook | Fully foreground and user-triggered | None | Same cloud-notebook limits; structured output is possible | Medium-low: page/API changes have broken Bookcision | Material but less agent-like than background automation | Viable low-cost prototype |
| Headless Playwright/Selenium login | Potentially scheduled | Requires a high-value Amazon session or credentials | Same cloud-notebook limits | Low: MFA, CAPTCHA, bot detection, session expiry, DOM changes | High | Reject |
| Kindle app share/email | Manual per document | None | Useful for some personal documents not represented in cloud notebook | High when available, but manual | Low | Preserve as auxiliary import path |

## Evidence

### No supported highlights API

Readwise documents that Kindle does not provide developers an API for highlights and that its own automatic import uses a browser extension against the Kindle Notebook. **[verified from Readwise's implementation documentation; not a direct Amazon API inventory]**

No reviewed Amazon developer surface documented access to a customer's Kindle notes or highlights. Because Amazon does not publish an exhaustive list covering every private interface, the stronger absolute claim remains **[unknown]**; the product-relevant fact is that no supported public contract was identified.

### Readwise automation still requires a live user session

Readwise's Kindle import documentation says:

- its extension operates against `read.amazon.com` after the user connects and signs in;
- the extension does not receive the Amazon password;
- Amazon periodically logs the browser out, after which Readwise prompts the user to sign in again;
- Readwise follows up when sync has not succeeded for an extended period;
- only one Amazon account can be continuously connected in a browser at a time.

Therefore, "automatic" means opportunistic synchronization inside a valid browser session, not a server-side integration with stable credentials. **[verified]**

### Cloud Notebook coverage is incomplete

Readwise documents that cloud synchronization covers Kindle-store content but not all sideloaded or personal documents. It also documents Amazon's copyright export limit, which commonly truncates exported highlights after a portion of a book, and location-based rather than page-based positions. **[verified]**

The reviewed documentation does not establish that Kindle Notebook exposes chapter titles. **[unknown]** Chapter fidelity must be tested against authenticated DOM/network data from real books before the product model depends on it.

### Browser extraction is a moving target

Bookcision is an MIT-licensed, browser-executed exporter maintained by Readwise. It runs in the user's authenticated Kindle Notebook page and can produce structured output without handling Amazon credentials. Its repository history includes repairs after Amazon endpoint/CORS behavior changed, and its README explicitly warns that Amazon may not preserve the service. **[verified]**

This demonstrates both feasibility and maintenance risk. A Relego implementation would inherit an undocumented external contract. **[inference]**

### Unattended browser automation has elevated policy risk

Amazon's Conditions of Use prohibit data mining, robots, and similar extraction tools. The version reviewed, dated 2026-08-14, also requires software agents to identify themselves and forbids concealing automation, mimicking human interaction to evade detection, circumventing CAPTCHAs, or bypassing controls intended to restrict agents. **[verified]**

A scheduled headless browser that authenticates as the user and attempts to survive or bypass interactive challenges would create direct policy, account-security, and maintenance risk. **[inference]** Relego should not store Amazon credentials or automate MFA/CAPTCHA.

### Rate limits and enforcement are not documented

No official rate limit was found because no supported highlights API was found. Readwise documents session expiry rather than explicit throttling. Actual bot detection and enforcement against foreground extraction are **[unknown]**.

## Product implications

### Dogfooding

**[recommendation]** Prototype the smallest browser-assisted flow before building a packaged extension:

1. Inspect a real authenticated Kindle Notebook account for available fields, especially chapter information.
2. Adapt a bookmarklet or local extension to emit Relego's canonical import payload.
3. Require an explicit user action and show import counts, truncation warnings, and the last successful run.
4. Never store Amazon credentials or session cookies in Relego.
5. Measure whether this is materially easier than device/file import over several weeks.

### Open-source release

**[recommendation]** If dogfooding succeeds, publish browser-assisted import as an optional adapter with:

- narrow host permissions;
- no background login automation;
- explicit unsupported-contract and export-limit warnings;
- a tested parser boundary so Amazon page changes do not affect core ingestion;
- `My Clippings.txt` and connected-device import as stable fallbacks.

Do not market this adapter as unattended sync. A genuine sync promise should wait for a supported platform contract.

## Unknowns and falsification tests

| Unknown | Why it matters | Test |
| --- | --- | --- |
| Chapter data in Kindle Notebook DOM/network payloads | The roadmap requires chapter-aware annotations | Inspect several purchased books with known chapter boundaries and record raw fields |
| Current session lifetime and re-auth frequency | Determines day-to-day friction | Dogfood for 30 days and log every forced sign-in |
| Copyright export-limit behavior by title/region | Determines completeness | Compare Notebook output with device clippings for several books and regions |
| Browser-store acceptance of the minimum permission set | Determines distribution friction | Submit an unlisted prototype using only the required Kindle host and user gesture |
| Amazon enforcement against user-initiated extraction | Determines release risk | Seek legal review or explicit Amazon guidance; monitor established tools for policy changes |

The recommendation should be revisited if Amazon publishes a supported highlights API or explicitly authorizes this use case.

## Sources

All sources accessed 2026-09-20.

- Amazon, Conditions of Use: <https://www.amazon.com/gp/help/customer/display.html?nodeId=508088>
- Readwise, Import from Amazon Kindle: <https://docs.readwise.io/readwise/docs/importing-highlights/kindle>
- Readwise, Import Highlights overview: <https://docs.readwise.io/readwise/docs/importing-highlights>
- Readwise, Account Access FAQ: <https://docs.readwise.io/faqs/account>
- Bookcision source and README: <https://github.com/TristanH/bookcision>