# Product

## Register

product

## Users

One person, self-hosting. They run `relego-server` on a NAS, a Raspberry Pi, or a spare
box on their home network, and they are the only user — the API has no authentication
because it never leaves the LAN.

They are a reader first and an operator second. They touch Relego in two very different
modes:

- **Setup (rare, high stakes).** Once, after `docker compose up`: point it at a
  Send-to-Kindle address, paste SMTP credentials, choose a cadence, import their first
  highlight file. Everything is unfamiliar and everything can silently fail — a wrong SMTP
  port means no recap ever arrives and nothing tells them why.
- **Curation (occasional, low stakes).** Every few weeks, usually after finishing a book:
  import the new highlights, bump the weight on the passages that mattered, exclude the
  book they abandoned, rename a title the Kindle mangled.

They are technically comfortable — they chose a self-hosted tool over Readwise — but they
are not in this UI daily and will not remember how it works between visits.

## Product Purpose

Relego brings a reader's own Kindle and Kobo highlights back to them on a schedule, as a
native e-ink document rather than a phone notification. It exists because highlights are
write-only for most people: captured once, never revisited.

The web UI replaces a Terminal.Gui TUI that had grown too complex to navigate. Success is
that a first-time user gets from `docker compose up` to a delivered test recap without
reading documentation, and that a returning user can import a book and re-weight its
highlights without relearning the interface.

## Brand Personality

**Considered, unhurried, plainspoken.**

Relego is named from the Latin *relegere* — to read again, go over carefully. The product
should feel like the reading it serves: warm, paper-toned, calm, with nothing competing
for attention. It is a librarian, not a dashboard.

Copy is plain and specific. It says "That doesn't look like an email address — it needs a
domain, like me@inbox.com", not "Invalid input". It never says "Oops!". It never
celebrates. Success is confirmed once, quietly, and gets out of the way.

## Anti-references

- **SaaS analytics dashboards.** No hero metric blocks, no sparkline walls, no
  "engagement" framing. The user has 1,284 highlights; that is not a KPI.
- **Notion / Readwise density-by-default.** Relego does one thing. It should not grow a
  sidebar of features it doesn't have.
- **Terminal cosplay.** The web UI replaces a TUI; it must not imitate one. No monospace
  body text, no fake scanlines, no ASCII art chrome.
- **AI-generated landing-page grammar.** No gradient text, no glassmorphism, no uppercase
  tracked eyebrows above every section, no side-stripe accent borders, no identical card
  grids, no decorative motion.
- **Modal-first interaction.** The TUI it replaces used popups for detail views, weight
  editing and delete confirmation. The web UI resolves these inline.

## Design Principles

1. **Setup is the hardest screen, so it gets the most care.** Every credential field
   explains what will happen, every destination can be tested before it matters, and every
   failure names the thing that failed.
2. **The highlight is the content; everything else is chrome.** Quoted text is the only
   place a display face appears. Controls stay quiet so the words don't compete.
3. **Inline over modal.** A detail view, a weight change, a confirmation — resolve it in
   place. Interrupt only for genuinely global actions.
4. **Reversible beats confirmed.** Prefer an undo affordance after the fact over a
   are-you-sure gate before it.
5. **Keyboard is a first-class path, not an accessibility checkbox.** The people replacing
   a TUI expect to drive this without a mouse; the shortcut layer is part of the design,
   discoverable from a single `?`.

## Accessibility & Inclusion

- **WCAG 2.2 AA**, verified rather than assumed: automated axe-core sweeps on every route,
  in both themes, in CI.
- Contrast is checked against the *actual* token pairs. The brand accent `#b56b39` is only
  3.65:1 on the light canvas, so it is never used for small text there — a darker accent
  step carries text and solid button fills.
- Light, dark, and **system** themes are all first-class. The default follows the OS.
- Full keyboard operability: visible focus on every interactive element, a skip link,
  roving tabindex in lists, no keyboard traps, and shortcut handlers that stand down while
  focus is in a text field.
- Status is never carried by color alone — delivery results, exclusions and connection
  state all pair color with a label or icon.
- `prefers-reduced-motion` is honored throughout; every transition has a no-motion path.
- Live regions announce async outcomes (import results, test emails, undo toasts) so
  screen-reader users get the same feedback sighted users get from a toast.
