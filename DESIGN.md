---
name: Relego
description: Self-hosted Kindle highlight recaps, delivered back to the device you already read on.
colors:
  paper: "#f7f1e8"
  paper-panel: "#f0e8dc"
  ink: "#171311"
  ink-soft: "#665748"
  terracotta: "#b56b39"
  terracotta-deep: "#5f3417"
  terracotta-link: "#8f4c18"
  night: "#120e0c"
  night-panel: "#1a1512"
  bone: "#f5eee3"
  bone-soft: "#9d8d7d"
  amber: "#d4a05e"
  amber-link: "#e0ab6b"
typography:
  display:
    fontFamily: "Playfair Display, Georgia, Times New Roman, serif"
    fontSize: "clamp(2.25rem, 1.6rem + 2.4vw, 3.25rem)"
    fontWeight: 300
    lineHeight: 1.1
    letterSpacing: "-0.015em"
  headline:
    fontFamily: "Playfair Display, Georgia, Times New Roman, serif"
    fontSize: "clamp(1.5rem, 1.28rem + 0.9vw, 1.9rem)"
    fontWeight: 300
    lineHeight: 1.2
    letterSpacing: "-0.015em"
  title:
    fontFamily: "ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "-0.005em"
  body:
    fontFamily: "ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.75
    letterSpacing: "normal"
  label:
    fontFamily: "ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "0.1em"
  mono:
    fontFamily: "ui-monospace, Cascadia Mono, Segoe UI Mono, SF Mono, Menlo, Consolas, monospace"
    fontSize: "0.9em"
    fontWeight: 400
    lineHeight: 1.6
    letterSpacing: "normal"
rounded:
  none: "0"
  chip: "0.3rem"
  sm: "0.5rem"
  md: "0.6rem"
  pill: "50%"
spacing:
  xs: "0.25rem"
  sm: "0.65rem"
  md: "1rem"
  lg: "1.75rem"
  xl: "3rem"
components:
  button-primary:
    backgroundColor: "{colors.terracotta}"
    textColor: "#ffffff"
    rounded: "{rounded.sm}"
    padding: "0.625rem 1.25rem"
    typography: "{typography.body}"
  button-outline:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "0.625rem 1.25rem"
    typography: "{typography.body}"
  code-inline:
    backgroundColor: "{colors.paper-panel}"
    textColor: "{colors.ink}"
    rounded: "{rounded.chip}"
    padding: "0.1em 0.35em"
    typography: "{typography.mono}"
  table-header-cell:
    backgroundColor: "transparent"
    textColor: "{colors.ink-soft}"
    rounded: "{rounded.none}"
    padding: "0.65rem 1rem 0.65rem 0"
    typography: "{typography.label}"
  station-marker:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink-soft}"
    rounded: "{rounded.pill}"
    size: "2.25rem"
    typography: "{typography.display}"
  stagemark:
    backgroundColor: "transparent"
    textColor: "{colors.ink-soft}"
    rounded: "{rounded.none}"
    padding: "0 0 0.75rem"
    typography: "{typography.label}"
---

# Design System: Relego

## Overview

**Creative North Star: "The Paper Circuit"**

Relego is a tool for people who underline books and want those sentences back. Its
interface behaves like the object it serves: warm paper stock, a serif that belongs
on a title page, and nothing that looks like a SaaS dashboard. The ground is a
cream-to-espresso paper tone that flips wholesale between light and dark; the single
accent is a fired terracotta that warms to amber at night. There is exactly one
accent, and it is spent sparingly — on the thing you are meant to do next, and on the
line that closes the loop.

The second half of the north star is the circuit. Relego's product truth is a round
trip: a highlight leaves your reader, passes through hardware you own, and comes back
to the same screen. The documentation surface makes that literal — four numbered
stations on a vertical spine, with a dashed return leg in the outer gutter carrying
the last station back to the first. That geometry is not decoration on one page; a
stop marker repeats it at the head of every stage page so the reader always knows
where on the circuit they are standing.

Structurally the system is subtractive. Separation is done with hairline rules and
generous vertical space, not with cards, panels, or filled containers. Surfaces are
flat; the only depth in the system is a barely-there wash behind the page head. Where
a conventional docs theme would reach for a boxed callout, Relego reaches for a rule
and more air.

**Key Characteristics:**

- Warm paper ground in both themes; never neutral gray, never pure white or black
- One accent, used at low density, mostly on next-actions and the return leg
- Playfair Display Light for display and headline; system sans for everything read at length
- Hairline rules (1px, 12–14% ink) instead of card chrome
- The round trip is a first-class visual device, not an illustration

## Colors

A single fired-clay accent on a warm paper ground, mirrored rather than inverted
between light and dark.

### Primary

- **Fired Terracotta** (`#b56b39`): The one accent in light theme. Primary buttons,
  the active-page marker in the sidebar, station markers on hover, the return leg of
  the circuit, and the current stop indicator.
- **Lamp Amber** (`#d4a05e`): The same accent at night. Warmer and lighter so it
  reads as lamplight against espresso rather than as a brighter version of the day
  color.

### Neutral

- **Paper** (`#f7f1e8`): The light-theme ground. Everything sits directly on it.
- **Panel Paper** (`#f0e8dc`): One step down from the ground; code frames and inline
  code chips.
- **Ink** (`#171311`): Light-theme body and heading text. Warm near-black, never
  `#000`.
- **Soft Ink** (`#665748`): Light-theme secondary text — station descriptions, table
  headers, the stop marker label. Measured at 6.19:1 on Paper.
- **Espresso** (`#120e0c`): The dark-theme ground.
- **Espresso Panel** (`#1a1512`): Dark-theme code frames and raised chrome.
- **Bone** (`#f5eee3`): Dark-theme body and heading text.
- **Soft Bone** (`#9d8d7d`): Dark-theme secondary text. Measured at 5.98:1 on
  Espresso.

### Link colors

Links deviate from the accent so they stay legible as running text: `#8f4c18`
(5.82:1 on Paper) in light, `#e0ab6b` (9.33:1 on Espresso) in dark. The raw accent is
for surfaces and markers, not for underlined text at body size.

### Named Rules

**The One Accent Rule.** There is exactly one accent per theme. No success green, no
danger red, no info blue. Status is carried by the aside's icon and label, not by a
second hue.

**The Warm Neutral Rule.** Every neutral in the ramp carries the paper's hue. A
neutral that reads as cool gray is out of system, including borders and dividers.

**The Sparing Accent Rule.** Accent coverage stays under roughly 5% of any viewport.
When two things on a screen both want it, one of them is not the next action.

## Typography

**Display Font:** Playfair Display Light 300 (with Georgia, Times New Roman, serif)
**Body Font:** system sans stack (`ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto`)
**Mono Font:** system mono stack (`ui-monospace, Cascadia Mono, SF Mono, Menlo, Consolas`)

**Character:** A book title set against a plain reading face. Playfair at weight 300
with negative tracking gives every page a printed masthead; the system sans below it
disappears so that instructions read as instructions. The contrast between the two is
the entire typographic idea — there is no third voice.

### Hierarchy

- **Display** (300, `clamp(2.25rem, 1.6rem + 2.4vw, 3.25rem)`, `-0.015em`, balanced
  wrap): page `h1` only. One per page.
- **Headline** (300, `clamp(1.5rem, 1.28rem + 0.9vw, 1.9rem)`, `-0.015em`): section
  `h2`. Always preceded by a full-width hairline rule and 3rem of space.
- **Title** (600, `1.25rem`): `h3` and station names. Sans, not serif — this is where
  the serif stops.
- **Body** (400, `1rem`, line-height 1.75, max 72ch): running prose. Content column is
  capped at `48rem`.
- **Label** (600, `0.75rem`, `0.1em`, uppercase): sidebar group headings, table
  headers, the round-trip stop marker.
- **Mono** (400, `0.9em`): commands, paths, environment variables, settings keys.

### Named Rules

**The Two Voices Rule.** Serif for display and headline, sans for everything read at
length. A serif `h3` or a sans `h1` is out of system.

**The Nowrap Command Rule.** Inline code inside a table cell never wraps. If the table
no longer fits, the table scrolls — the command does not break across lines.

## Layout

A single content column with a fixed comfortable measure, not a fluid one. Docs
content is capped at `48rem`; prose and list items are further capped at `72ch` and
station descriptions at `52ch`. The docs shell is a three-column arrangement at desktop
(sidebar / content / on-this-page), collapsing to a single column with a disclosure
header below the tablet breakpoint.

Vertical rhythm is coarse and consistent: `3rem` above an `h2`, `2.25rem` above an
`h3`, `1.75rem` between the rule and the heading it introduces. Density is low on
purpose — this is a Read surface, and the whitespace is doing the job that boxes do
elsewhere.

Responsive behavior:

- Below `50rem`, wide reference tables become their own horizontal scroll box with a
  floor width scaled by column count (`30rem` / `40rem` / `52rem`). The page itself
  never scrolls sideways, and a scrollable table is keyboard focusable.
- Below `30rem`, the round trip drops its outer return-leg bracket and lets the
  closing line carry the loop; the stop marker steps down one type size.
- Long shell commands opt into soft wrapping rather than overflowing.

## Elevation & Depth

**The system is flat.** There are no elevation levels and no shadow scale for content.
Depth is conveyed by tonal separation (`paper` vs `paper-panel`) and hairline rules,
never by lifting a surface. Two exceptions exist and are both atmospheric rather than
structural: a wide, very low-opacity accent wash behind the top of the page, and a
`blur(12px)` backdrop on the sticky header so text passing beneath it stays legible.

### Named Rules

**The Flat Surface Rule.** Content surfaces cast no shadow at rest or on hover. If an
element needs to feel interactive, it changes tone and shifts 2px, it does not lift.

## Shapes

Corners are small and quiet: `0.5rem` on buttons and asides, `0.6rem` on the round-trip
station hit area, `0.3rem` on inline code chips, `0` on code frames so the terminal
title bar stays fused to its body. Circles are reserved: the station markers and the
stop-marker dots are the only fully round elements in the system, which is what makes
the circuit read as a circuit.

Borders are always `1px` and always the rule token — `rgba(23,19,17,0.12)` in light,
`rgba(245,238,227,0.14)` in dark. There is no heavy border, no double rule, and no
border used for emphasis.

## Components

### Buttons

- **Shape:** small radius (`0.5rem`)
- **Primary:** accent fill, white text, `0.625rem 1.25rem`
- **Hover / Focus:** primary drops to 90% opacity; focus draws a 2px accent ring
  offset from the page ground
- **Outline:** hairline border, ink text, translucent surface fill on hover

### Code

- **Inline:** panel-paper chip, hairline border, `0.3rem` radius, mono at `0.9em`
- **Blocks:** Expressive Code terminal frame with square corners; never restyle the
  frame's radius independently of its title bar

### Tables

Rules only — no zebra striping, no cell borders, no container. Header cells use the
label style in soft ink; body cells are top-aligned with the leading edge flush to the
text column. Below `50rem` the table becomes its own scroll region with a focus ring.

### Navigation

Sidebar groups are labels in soft ink; items are body sans. The current page is marked
with a `2px` inset accent bar on its leading edge and a weight bump — never a filled
pill or a background block.

### The Round Trip (signature component)

The system's one signature composition. An ordered list of four stations on a `1px`
vertical spine, each with a Playfair numeral in a circular hairline marker in the
gutter. A dashed accent bracket runs up the outer gutter from the last station to the
first, terminating in a chevron, and a ringed accent dot closes the list with the
return line. Hovering a station tints its background and warms its marker to accent;
the station itself does not move.

Its companion, the **stop marker**, appears above the `h1` on each of the four stage
pages: five small dots with the current one filled in accent and haloed, plus a
`Stop N of 5 on the round trip` label. This is how the circuit survives past the index
page.

## Do's and Don'ts

### Do:

- **Do** use hairline rules and vertical space to separate sections, at the exact
  values above (`1px`, rule token, `3rem` / `1.75rem`).
- **Do** set every page `h1` and section `h2` in Playfair Display 300 with `-0.015em`
  tracking and balanced wrapping.
- **Do** keep the accent under roughly 5% of any viewport, and reserve it for the next
  action, the current position, and the return leg.
- **Do** use the link colors (`#8f4c18` / `#e0ab6b`) for underlined running text and
  the raw accent for surfaces and markers.
- **Do** let wide tables scroll inside their own focusable box rather than shrinking
  or wrapping the commands inside them.
- **Do** carry the round-trip geometry onto any page that belongs to a stage.

### Don't:

- **Don't** introduce a second accent hue for status, category, or emphasis.
- **Don't** put content in cards, filled panels, or bordered boxes; the aside is the
  only permitted container and it is a rule plus a tint, not a card.
- **Don't** add `box-shadow` to content surfaces, including on hover.
- **Don't** use cool or pure neutrals — no `#000`, no `#fff` as a ground, no gray that
  has lost the paper's warmth.
- **Don't** set body copy, `h3`, or UI labels in the serif.
- **Don't** override the code frame's corner radius on its own; the frame and its
  title bar are one object.
