---
name: Relego
description: Self-hosted highlight recaps, delivered to your e-reader.
colors:
  accent: "#b56b39"
  accent-ink: "#8f5228"
  accent-solid: "#9a5a2f"
  accent-solid-hover: "#874e28"
  accent-dark: "#d4a05e"
  accent-dark-hover: "#e0b073"
  canvas: "#f7f1e8"
  canvas-dark: "#120e0c"
  surface: "#fffcf7"
  surface-dark: "#1b1613"
  surface-sunk: "#f0e7d9"
  surface-sunk-dark: "#0c0908"
  rail: "#f1e8db"
  rail-dark: "#0e0b0a"
  ink: "#171311"
  ink-dark: "#f5eee3"
  ink-muted: "#5f5349"
  ink-muted-dark: "#bdb1a4"
  ink-subtle: "#736558"
  ink-subtle-dark: "#9a8d7f"
  success: "#26694a"
  success-dark: "#74c496"
  danger: "#a33127"
  danger-dark: "#ef8d81"
  warning: "#7d5a10"
  warning-dark: "#e3b75f"
  border: "rgba(23,19,17,0.14)"
  border-dark: "rgba(245,238,227,0.14)"
  border-strong: "rgba(23,19,17,0.30)"
  border-strong-dark: "rgba(245,238,227,0.30)"
  scrim: "rgba(23,19,17,0.45)"
  scrim-dark: "rgba(6,4,4,0.66)"
typography:
  quote:
    fontFamily: "Playfair Display, Georgia, Times New Roman, serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.55
    letterSpacing: "normal"
  wordmark:
    fontFamily: "Playfair Display, Georgia, serif"
    fontSize: "1.5rem"
    fontWeight: 400
    lineHeight: 1.2
    letterSpacing: "-0.015em"
  heading:
    fontFamily: "ui-sans-serif, -apple-system, Segoe UI, Inter, Roboto, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.01em"
  body:
    fontFamily: "ui-sans-serif, -apple-system, Segoe UI, Inter, Roboto, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  label:
    fontFamily: "ui-sans-serif, -apple-system, Segoe UI, Inter, Roboto, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 600
    lineHeight: 1.4
    letterSpacing: "normal"
  mono:
    fontFamily: "ui-monospace, SF Mono, Cascadia Mono, JetBrains Mono, Consolas, monospace"
    fontSize: "0.75rem"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "normal"
rounded:
  xs: "3px"
  sm: "6px"
  md: "9px"
  lg: "14px"
  pill: "999px"
spacing:
  "0": "2px"
  "1": "0.25rem"
  "2": "0.5rem"
  "3": "0.75rem"
  "4": "1rem"
  "5": "1.5rem"
  "6": "2rem"
  "7": "3rem"
components:
  button-primary:
    backgroundColor: "{colors.accent-solid}"
    textColor: "#ffffff"
    rounded: "{rounded.md}"
    padding: "7px 16px"
    typography: "{typography.label}"
  button-primary-hover:
    backgroundColor: "{colors.accent-solid-hover}"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
    padding: "7px 16px"
  button-secondary-hover:
    backgroundColor: "{colors.surface-sunk}"
  button-ghost:
    backgroundColor: "#00000000"
    textColor: "{colors.ink-muted}"
    rounded: "{rounded.md}"
    padding: "7px 16px"
  input:
    backgroundColor: "{colors.canvas}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
    padding: "7px 12px"
  panel:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.lg}"
    padding: "1.5rem"
  nav-item:
    backgroundColor: "#00000000"
    textColor: "{colors.ink-muted}"
    rounded: "{rounded.md}"
    padding: "0.5rem 0.75rem"
  nav-item-active:
    backgroundColor: "#b56b391a"
    textColor: "{colors.ink}"
  tag:
    backgroundColor: "#00000000"
    textColor: "{colors.ink-muted}"
    rounded: "{rounded.pill}"
    padding: "2px 8px"
  kbd:
    backgroundColor: "{colors.surface-sunk}"
    textColor: "{colors.ink-subtle}"
    rounded: "{rounded.sm}"
    padding: "3px 5px"
---

# Design

## 1. Overview

**Creative North Star: "A reading lamp, not a control room."**

Relego is a self-hosted tool that returns a reader's own highlights to them on a schedule.
Its interface serves that act and nothing else: warm paper and ink neutrals, a single
terracotta accent used sparingly, and a quiet, dense product chrome that gets out of the
way of the quoted text. The system is **Restrained** — one accent, tinted neutrals, no
second brand color competing for attention. Density is moderate: tables carry many rows
because a library of 40 books and 1,200 highlights is real data, but nothing is
decorated to fill space.

Two registers coexist in this repository and they are deliberately different. The
**landing page** (`src/landing`) is a brand surface: display serif, generous scale, a
single flat warm background. The **web UI** (`src/web`) is a product surface: it inherits
the exact brand hues but rebuilds them into a layered ramp with real elevation
(canvas → rail → surface → sunk), and it swaps the display face out of the chrome
entirely. Sharing the palette is what makes them the same product; sharing the *typography
system* would make the app harder to use.

This system explicitly rejects: SaaS dashboard grammar (hero metric blocks, sparkline
walls, "engagement" framing), terminal cosplay (monospace body, ASCII chrome, fake
scanlines — the web UI replaces a TUI and must not imitate one), AI landing-page grammar
(gradient text, glassmorphism, uppercase tracked eyebrows, side-stripe accent borders,
identical card grids), and modal-first interaction.

**Key Characteristics:**

- Warm paper-and-ink neutrals in both themes; the hue is the brand, the layering is the product.
- One accent, one job: primary action, current selection, state indicator. Never decoration.
- Serif appears exactly twice: the wordmark, and quoted highlight text.
- Flat at rest; shadow is a response to state, not a default.
- Light, dark and system are all first-class; system is the default.

## 2. Colors

A warm terracotta-and-parchment palette carried from the landing page, re-cut into four
surface layers plus a three-step ink ramp so dense product UI has somewhere to sit.

### Primary

- **Terracotta** (`#b56b39` light / `#d4a05e` dark): the brand accent. Used for fills that
  are large enough to read as shape — the selection bar on a table row, the "on" segments
  of a weight control, the focus ring, the drop-zone border on hover, the active nav wash.
  In **light mode it must never carry small text or sit behind white text**: it is 3.65:1
  on `#f7f1e8` and 4.10:1 behind `#ffffff`. Two derived steps exist for exactly that reason.
- **Terracotta Ink** (`#8f5228` light / `#d4a05e` dark): accent *as text*. 5.5:1 on the
  light canvas, 8.1:1 on the dark. Used for the active nav icon, step numerals, inline
  accent links, and the callout icon.
- **Terracotta Solid** (`#9a5a2f` light / `#d4a05e` dark): accent *as a button background*.
  5.4:1 with white in light mode, 7.9:1 with `#171311` in dark. This is the only fill a
  primary button uses.
- **Accent Wash** (`rgba(181,107,57,0.10)` light / `rgba(212,160,94,0.14)` dark): the
  low-commitment accent. Row hover, active nav background, palette highlight.

### Neutral

- **Canvas** (`#f7f1e8` / `#120e0c`): the app background and the base for input fills.
  Identical to the landing page's `--color-bg`; this is the shared identity anchor.
- **Rail** (`#f1e8db` / `#0e0b0a`): the second neutral layer. Left navigation, mobile tab
  bar. Half a step off canvas — enough to read as a different plane, not enough to look
  like a separate app.
- **Surface** (`#fffcf7` / `#1b1613`): panels, tables, expanded highlight rows, dialogs.
  The plane content lives on.
- **Surface Sunk** (`#f0e7d9` / `#0c0908`): table headers, `<kbd>` chips, segmented-control
  troughs, secondary-button hover. Recessed, never interactive on its own.
- **Ink** (`#171311` / `#f5eee3`): primary text. Unchanged from the landing page.
- **Ink Muted** (`#5f5349` / `#bdb1a4`): secondary text, inactive nav labels, table cell
  support text. 6.7:1 / 9.1:1 on canvas.
- **Ink Subtle** (`#736558` / `#9a8d7f`): tertiary text — timestamps, hint lines, locations,
  placeholders. 5.0:1 / 5.9:1 on canvas. **This is the floor.** Nothing lighter carries text.
- **Border** (`rgba(23,19,17,0.14)` / `rgba(245,238,227,0.14)`) and **Border Strong**
  (`0.30` alpha in both): hairlines and control strokes respectively.

### Semantic

- **Success** (`#26694a` / `#74c496`): delivered recaps, passing connection tests, "in recaps".
- **Danger** (`#a33127` / `#ef8d81`): destructive actions and validation failures.
- **Warning** (`#7d5a10` / `#e3b75f`): excluded items and degraded-but-not-broken states.

### Named Rules

**The Two-Accent-Steps Rule.** `#b56b39` is a shape color, not a text color. In light mode,
any accent that carries a glyph uses `accent-ink`; any accent behind a glyph uses
`accent-solid`. Writing `color: var(--accent)` on light text is a bug, not a style choice.

**The Ink Floor Rule.** `ink-subtle` is the lightest text in the system. There is no fourth,
lighter step. If text feels too loud, cut the text — don't fade it.

**The One Voice Rule.** The accent covers well under 10% of any screen. Its rarity is what
makes a selected row or a primary button legible at a glance.

## 3. Typography

**Display Font:** Playfair Display (with Georgia, Times New Roman, serif)
**Body Font:** system UI stack — `ui-sans-serif, -apple-system, "Segoe UI", Inter, Roboto, sans-serif`
**Label/Mono Font:** `ui-monospace, "SF Mono", "Cascadia Mono", "JetBrains Mono", Consolas, monospace`

**Character:** A true contrast pairing — a warm transitional serif against a neutral
system sans. The serif is not the interface's voice; it is the *content's* voice. It
appears on the wordmark and on quoted highlight text, and nowhere else. Everything a user
clicks, reads as a label, or scans in a table is set in the system sans, so the UI inherits
the platform's own rendering and hinting.

The scale is **fixed rem, not fluid**. Product UI is viewed at consistent DPI, and a
`clamp()` heading that shrinks inside a narrow panel looks worse, not better. Seven steps,
no more: `0.6875 / 0.75 / 0.8125 / 0.875 / 1 / 1.25 / 1.5rem`. The bottom of the ramp is
deliberately tight (many small roles need to sit at the same optical weight), while the top
opens up to a 1.2–1.33 ratio so a view title is unmistakably a view title.

### Hierarchy

- **Display / Wordmark** (Playfair Display 400, 1.5rem, 1.2, `-0.015em`): the `relego.`
  mark in the rail and on the mobile header. The trailing period is `accent`.
- **Quote** (Playfair Display 400, 1rem, 1.55, `text-wrap: pretty`): highlight text. Capped
  at 72ch. Clamped to 2 lines when a highlight row is collapsed, full when expanded.
- **Headline** (sans 600, 1.5rem, 1.25, `-0.01em`, `text-wrap: balance`): view titles.
- **Section** (sans 600, 1.25rem, 1.3): the largest heading inside a view.
- **Title** (sans 600, 1rem, 1.3): panel and section headings.
- **Body** (sans 400, 0.875rem, 1.5): the default. Prose capped at 62–75ch; table cells and
  dense controls run wider.
- **Label** (sans 600, 0.8125rem, 1.4): form labels, table headers, button text. **Sentence
  case, never uppercase-with-tracking.**
- **Caption** (sans 400, 0.75rem, 1.4, `ink-subtle`): hints, timestamps, locations, help text.
- **Micro** (sans 600, 0.6875rem, 1): key caps, mobile tab labels, environment badges. Never
  used for anything a user has to read as a sentence.
- **Mono** (0.75rem): only for literal machine strings, such as file names, host:port, paths
  and environment variable names. Never for prose.

### Named Rules

**The Serif-Is-Content Rule.** If a user can click it, type into it, or scan it as a label,
it is sans. Playfair appears on the wordmark and on the reader's own words. Two places.

**The No-Eyebrow Rule.** No tiny uppercase tracked kicker above sections. Panels are titled
in sentence case at Title size, or they are not titled.

## 4. Elevation

The system is **tonally layered, not shadowed**. Depth comes from four background steps
(canvas → rail → surface → sunk) plus 1px hairline borders. At rest, nothing floats:
tables, panels and highlight rows sit flat on the canvas with a border and a 14px radius.

Shadow is reserved for genuine z-axis events — an element that has left the document flow
(dialog, toast) or a control that has physically risen (the active segment of a theme
switch). This keeps the interface calm at rest and makes an actual overlay unmistakable.

### Shadow Vocabulary

- **Raised** (`box-shadow: 0 1px 2px rgba(23,19,17,0.06), 0 1px 1px rgba(23,19,17,0.04)`;
  dark: `0 1px 2px rgba(0,0,0,0.4)`): the selected thumb of a segmented switch. Nothing else.
- **Overlay** (`box-shadow: 0 12px 32px -12px rgba(23,19,17,0.28), 0 2px 8px rgba(23,19,17,0.08)`;
  dark: `0 16px 40px -14px rgba(0,0,0,0.72), 0 2px 8px rgba(0,0,0,0.4)`): command palette,
  shortcut sheet, toasts.

### Named Rules

**The Flat-At-Rest Rule.** A surface gets a shadow only when it has actually left the page.
Hover raises color, not altitude.

**The z-index Scale Rule.** Only these values exist: `sticky 100`, `rail 200`,
`backdrop 300`, `modal 400`, `toast 500`. No `999`, no `9999`.

## 5. Components

### Buttons

- **Shape:** softly rounded rectangle (`9px`), 7px/16px padding, 0.8125rem 500-weight label.
- **Primary:** `accent-solid` fill, white text in light / `#171311` in dark, transparent
  border. One primary per view — "Send recap now", "Save changes", "Import highlights".
- **Secondary (default):** `surface` fill, `border-strong` 1px stroke, `ink` text. The
  workhorse.
- **Ghost:** transparent, `ink-muted` text; hover fills with `accent-wash`. Toolbar and
  rail affordances only.
- **Danger:** secondary shape, `danger` text, hover fills with 12% `danger`. Never a solid
  red button — the confirmation step carries the weight, so the trigger doesn't get to shout.
- **Hover / Focus:** 180ms `cubic-bezier(0.22, 1, 0.36, 1)` on background and border only.
  Focus is a 2px `accent` outline with 2px offset — on every interactive element, no
  exceptions.
- **Loading:** `aria-busy="true"`, a 13px `currentColor` ring spinner prepended, label
  dimmed to 60%. The button keeps its width; nothing reflows.

### Chips / Tags

- **Style:** pill (`999px`), transparent fill, 1px `border-strong` stroke, 0.75rem 550
  weight, `ink-muted` text.
- **State:** tone-mapped by meaning, always with a text label so color is never the sole
  carrier — `In recaps` (success stroke), `Excluded` (warning stroke), `Delivered`,
  `SMTP auth failed`.

### Cards / Containers

- **Corner Style:** `14px` for panels and tables, `9px` for controls, `6px` for chips and kbd,
  `3px` for the smallest indicators (weight bars).
- **Background:** `surface`.
- **Shadow Strategy:** none — see Elevation. Borders and tone do the work.
- **Border:** 1px `border`; `border-strong` on hover for interactive containers,
  `accent` when expanded.
- **Internal Padding:** `1.5rem` for panels, `1rem` for table cells and highlight rows.
- **Never nested.** A panel does not contain another panel. A table is not inside a card.

### Inputs / Fields

- **Style:** `canvas` fill (recessed against `surface`), 1px `border-strong` stroke, `9px`
  radius, 0.5rem/0.75rem padding, full width within its field.
- **Focus:** 2px `accent` outline at 1px offset plus the border shifting to `accent`.
- **Error:** `aria-invalid="true"` → `danger` border, plus a `danger` message beneath.
  The message names the fix, not the failure.
- **Read-only / env-managed:** `surface-sunk` fill, `ink-muted` text, `not-allowed` cursor,
  and a "from env" badge on the label.
- **Secrets:** password fields are write-only — the server never returns the value, and the
  field shows a fixed-length mask.
- Every field is `label` + control + optional `help` caption, in that order. Placeholders
  are examples, never labels.

### Navigation

- **Style:** 244px left rail on `rail`, 1px right border, items at 0.5rem/0.75rem with an
  18px stroke icon, 0.875rem 500-weight label, and an optional tabular-nums count on the right.
- **Default / hover / active:** `ink-muted` → `accent-wash` + `ink` → `accent-wash` + `ink`
  at 600 with an `accent-ink` icon and `aria-current="page"`.
- **Rail foot:** shortcut-sheet trigger, connection pill (LED + label + `host:port` in mono),
  and the three-way theme switch.
- **Mobile (≤900px):** the rail is replaced by a fixed 4-item bottom tab bar with
  `env(safe-area-inset-bottom)` padding; icons above 11px labels; active item in `accent-ink`.

### Signature Component — the highlight row

The replacement for the TUI's detail popup, and the core interaction of the product.

Collapsed, a row is a `surface` panel showing the quote clamped to two lines in Playfair,
a caption line (location, delivery count, exclusion tag), and a five-bar weight indicator
on the right (6×14px bars, `accent` when on, `border-strong` when off). The whole summary
is a single `<button>` with `aria-expanded`, so it is one tab stop and one Enter press.

Expanded, the row border turns `accent`, the quote unclamps, and an action strip appears
beneath a hairline: a 1–5 segmented weight control (`aria-pressed`, keys `1`–`5`), an
exclude/include toggle, and a danger-text Delete. **Nothing opens.** The list keeps its
scroll position and the user keeps their place.

Delete is the one destructive action with no server-side undo — `DELETE /highlights/{id}` is a
hard delete and there is no restore endpoint. So it takes two deliberate steps *in place*:
Delete swaps the button row for "Delete permanently? · Yes, delete · Keep". Still no dialog,
still no lost scroll position. `Escape` backs out of the confirmation before it collapses the
row, and collapsing or moving to another row abandons a pending confirmation.

Reversible actions get the opposite treatment: exclude/include acts immediately and raises a
toast with an Undo button, announced via `role="status"`.

### Measure

Two caps keep wide screens from stranding content:

- `--measure-view` (1180px) is applied as a shared `--gutter` on `.main`, so `.topbar` and
  `.view` centre on the same column. The topbar keeps a full-bleed background while its
  contents line up with the view beneath it. On mobile the `max()` collapses to `--sp-5`.
- `--measure-prose` (68ch) caps standalone paragraphs (`.view > p`, `.panel > p`,
  `.panel > header > p`). Text inside tables, rows and controls is laid out by its own
  component and is deliberately exempt.

`.hl-list` is additionally capped at 800px: the quote is capped at 72ch, so a wider card would
leave the weight pips stranded at the far edge of empty space.

## 6. Do's and Don'ts

### Do:

- **Do** use `accent-ink` (`#8f5228`) whenever the accent carries a glyph in light mode, and
  `accent-solid` (`#9a5a2f`) whenever a glyph sits on the accent. Raw `#b56b39` is a shape color.
- **Do** keep `ink-subtle` (`#736558` / `#9a8d7f`) as the lightest text in the system. Placeholders
  use it too — they are not exempt from 4.5:1, so the base rule is a bare `::placeholder`
  selector with `opacity: 1`, never a per-component override that some inputs miss.
- **Do** flip a focus ring inset (`outline-offset: -2px`) on any control that sits flush inside
  an `overflow: hidden` parent — table rows and `.hl-summary`. An outset ring on a flush child
  is clipped away entirely, which reads as "no focus ring" to a keyboard user.
- **Do** guard an action in **every** surface that can trigger it. A guard on the page button
  and none in the command palette just moves the failure; on mobile the palette *is* the
  primary action surface.
- **Do** resolve detail, weight editing and confirmation **inline**. The TUI's popups were the
  problem being solved; reproducing them as `<dialog>` is a regression.
- **Do** make destructive actions reversible where the server allows it (undo toast) and
  two-step where it does not (inline confirm). Never one-click-and-gone.
- **Do** send every partial `PATCH /settings` with the current `deliveryEmail`. The server
  treats an absent `deliveryEmail` as "clear it", so a Schedule-only save would otherwise wipe
  the user's delivery address.
- **Do** render server-scheduled times in the **server's** timezone with a matching label.
  The delivery time is configured in that zone; showing the viewer's local clock beside the
  server's zone name is a lie.
- **Do** pair every status color with a text label — `Delivered`, `Excluded`, `Disconnected`.
- **Do** give every interactive element all seven states: default, hover, focus-visible,
  active, disabled, loading, error.
- **Do** ship skeleton placeholders shaped like the content they replace, never a centered
  spinner in an empty region.
- **Do** write empty states that teach the next action ("Connect your Kindle by USB and drop
  its highlight file in") rather than reporting absence ("No data").
- **Do** hold transitions to 150–250ms on background, border, color, opacity and transform.
- **Do** provide a `prefers-reduced-motion: reduce` path for every animation, including the
  toast rise and the skeleton shimmer.
- **Do** stand every keyboard shortcut down while focus is inside an input, textarea or
  contenteditable.
- **Do** keep `src/web` visually consistent with `src/landing` through **color and the
  wordmark only**. The type systems are allowed — required — to differ.

### Don't:

- **Don't** use `background-clip: text` with a gradient. Anywhere. Emphasis is weight and size.
- **Don't** use `border-left` or `border-right` above 1px as a colored accent stripe on rows,
  panels or callouts. The selected table row uses `box-shadow: inset 2px 0 0` as a selection
  indicator paired with a background change and `aria-selected` — that is a selection state,
  not decoration, and it is the only inset bar in the system.
- **Don't** apply `backdrop-filter` or translucent "glass" surfaces. The landing page's
  `rgba(255,255,255,0.72)` surface does not come across; product panels are opaque.
- **Don't** write a tiny uppercase letter-spaced eyebrow above sections, or number them
  `01 / 02 / 03` unless the content is genuinely an ordered sequence (the Import instructions are).
- **Don't** build a hero metric block — big number, small label, supporting stats. "1,284
  highlights" is a caption on the Library header, not a display figure.
- **Don't** lay out books, highlights or settings as a grid of identical icon-heading-text
  cards. Books are a table. Highlights are a list. Settings are a form.
- **Don't** set monospace body text, ASCII art, scanlines or blinking cursors. This replaces
  a TUI; it does not cosplay one.
- **Don't** nest a card inside a card, or a panel inside a panel.
- **Don't** animate `width`, `height`, `top` or `left`. Transform and opacity, plus
  `background`/`border-color` for state.
- **Don't** use arbitrary z-index values. The scale is 100 / 200 / 300 / 400 / 500.
- **Don't** send an SMTP password back to the browser, and don't log it. The field is
  write-only in both directions.
