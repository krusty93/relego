# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Primary users are self-hosting readers who highlight while reading on a Kindle or Kobo and want to revisit those highlights instead of losing them. They are comfortable with a terminal, Docker Compose, and environment variables, but they are running Relego for personal use, not operating it as a service. Typical situation: they have just found the project, want to know whether it fits, and then need to get a first recap delivered without guessing.

A secondary audience is contributors extending the highlight-source layer. Their documentation stays in the repository (`CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, `docs/adr/`) and is explicitly out of scope for the end-user documentation site.

## Product Purpose

Relego imports highlights from a reading device, selects a subset on a schedule using spaced repetition weighted by user preferences, and delivers a recap back to the reader — as an EPUB through Amazon Send-to-Kindle, and/or as an HTML email to a regular inbox. Success is a reader opening a recap on the device they already read on and recalling something they had highlighted and forgotten.

The name comes from the Latin *relegere*: to read again, go over carefully, review.

## Positioning

E-ink first and self-hosted. Recaps arrive as native documents on the reading device rather than as phone notifications, there is no subscription and no data leaves the user's infrastructure, and the highlight-source layer is an open registry any contributor can extend. Highlights stay in an open format with no lock-in.

## Operating Context

- The user connects a Kindle or Kobo over USB and imports from `My Clippings.txt` or `.kobo/KoboReader.sqlite`.
- The server runs as a Docker container via `docker compose --profile app up -d`; a `demo` profile with smtp4dev exists for trying delivery without a real SMTP relay.
- Delivery requires an SMTP relay. Gmail and Outlook personal accounts no longer support SMTP password auth, so users need a relay such as AWS SES, Resend, MailerSend, or Mailgun.
- Amazon Send-to-Kindle only accepts mail from senders on the user's Approved Personal Document E-mail List — a real setup step outside the product.
- The CLI is installed via winget, an `install.ps1` / `install.sh` script, or run through Docker. Users work on Windows, macOS, and Linux, and device mount paths differ per OS.
- Documentation is read on phones as often as laptops (device in hand, terminal on the desk).

## Capabilities and Constraints

- Components: `relego` CLI, `relego-server` Docker container, and a self-hosted web UI (`src/relego.web`, React).
- Stack: C# / .NET 10, SQLite at `/data/relego.db`, Serilog, MailKit, Quartz.NET, REST HTTP with no auth in the MVP.
- Import sources today: Kindle and Kobo. If both are connected, both are imported in one run, and a per-source failure is reported without aborting the other.
- Delivery channels: Send-to-Kindle email (Kindle) and regular inbox HTML email (used by Kobo users, since Kobo has no Send-to-Kindle address). Both can be active at once.
- Default schedule is daily at 18:00 server local time; recap size defaults to 5 highlights and is configurable from 1 to 15.
- Exclusions can be set per highlight, book, or author; highlights can be weighted 1–5; books can be renamed.
- Docker image provenance is verifiable with `gh attestation verify`.
- The interactive TUI is deprecated and being removed; it must not be documented as a current capability.
- The marketing site lives in `src/landing` (Astro 7, Tailwind 4, Vercel Analytics/Speed Insights, Playwright tests including axe accessibility checks). The end-user documentation belongs on the same site at `/docs`.
- License: MIT.

## Brand Commitments

- Name `Relego`, lowercase logotype `relego.`
- Existing palette and theming are the incumbent authority: light `#f7f1e8` / `#171311` / accent `#b56b39`, dark `#120e0c` / `#f5eee3` / accent `#d4a05e`, documented in `docs/BRAND_COLORS.md`. Light/dark toggle with persisted preference already exists.
- Display typeface: Playfair Display, Light (300), used for headings alongside a neutral body face.
- Voice: plain, direct, practical. No hype, no invented endorsement.

## Evidence on Hand

- `README.md` — the current end-user documentation being moved to the site (install, configure, CLI reference, troubleshooting, supply-chain verification).
- `docs/assets/` — `tui-demo.gif`, `tui.png` (both tied to the deprecated TUI), `web-ui-light.png`, `web-ui-dark.png`, `relego-logo-banner.png`.
- `docs/examples/kindle-highlights.txt` and `docs/examples/kobo-highlights.sqlite` — real sample data users can run against with no device.
- `src/landing/assets/hero-kindle.jpg`.
- No testimonials, customer names, install counts, benchmarks, pricing, or uptime claims exist. None may be invented.

## Product Principles

1. **The device is the destination.** Every feature ends with something readable on e-ink, not on a phone.
2. **Self-hosted means the user stays in control.** No cloud dependency, no account, no data leaving their infrastructure.
3. **Honest about setup.** Real friction (SMTP relays, Amazon's approved-sender list, OS-specific mount paths) is stated plainly rather than hidden.
4. **Extensible by design.** New highlight sources are a documented contract, not a fork.
5. **Nothing invented.** Claims stay within what the project actually does today.

## Accessibility & Inclusion

The landing site already enforces axe-based accessibility checks in Playwright; new surfaces must hold the same bar. Documentation must be fully usable on mobile, laptop, and desktop, and must respect `prefers-reduced-motion` and the existing light/dark theme preference.
