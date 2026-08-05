# Relego Brand Colors

This file is the shared source of truth for color values used across the landing page, the TUI, and the web UI.

## Canonical palette

### Light

- background: `#f7f1e8`
- text: `#171311`
- accent: `#b56b39`
- surface: `rgba(255,255,255,0.72)`
- border: `rgba(23,19,17,0.12)`

### Dark

- background: `#120e0c`
- text: `#f5eee3`
- accent: `#d4a05e`
- surface: `rgba(18,14,12,0.82)`
- border: `rgba(245,238,227,0.14)`

## Product ramp (web UI)

The canonical palette is an identity palette: one background, one text color, one accent, and translucent surfaces. Dense product UI needs more than that — table headers, inputs, and nav need to sit on distinguishable layers, and translucency over a busy page reduces text contrast unpredictably. `src/relego.web/src/styles/tokens.css` therefore extends the canonical values into an opaque ramp. Background, text, and accent are unchanged; everything else is derived.

| Token | Light | Dark | Role |
|---|---|---|---|
| `--canvas` | `#f7f1e8` | `#120e0c` | page background (canonical) |
| `--surface` | `#fffcf7` | `#1b1613` | panels, tables, cards |
| `--surface-sunk` | `#f0e7d9` | `#0c0908` | table headers, inputs, `kbd` |
| `--rail` | `#f1e8db` | `#0e0b0a` | navigation rail |
| `--ink` | `#171311` | `#f5eee3` | body text (canonical) |
| `--ink-muted` | `#5f5349` | `#bdb1a4` | secondary text — 6.7:1 / 9.1:1 on canvas |
| `--ink-subtle` | `#736558` | `#9a8d7f` | tertiary text — 5.0:1 / 5.9:1 on canvas |
| `--accent` | `#b56b39` | `#d4a05e` | fills, indicators, focus (canonical) |
| `--accent-ink` | `#8f5228` | `#d4a05e` | accent as small text — 5.5:1 / 8.1:1 |
| `--accent-solid` | `#9a5a2f` | `#d4a05e` | accent as button background — 5.4:1 / 7.9:1 with its on-color |

The accent splits into three tokens for the same reason the TUI splits `Accent` and `AccentText`: `#b56b39` is only 3.65:1 on the light canvas and 4.10:1 behind white text, so it is safe as a fill but not as small text or as a button background. Dark mode needs no split — `#d4a05e` clears AA in all three roles — but the tokens exist in both themes so components never branch on theme.

## Semantic mapping in TUI

`src/Relego.Cli/Tui/TuiTheme.cs` maps the canonical palette to terminal-friendly tokens:

- `Background`, `Text`, `TextMuted`
- `Accent` and `AccentText` (contrast-safe accent for small text)
- `Border`, `BorderFocus`
- `Success`, `Error`, `Warning`

The mode is selected via `RELEGO_THEME`:

- `RELEGO_THEME=dark` (default)
- `RELEGO_THEME=light`

## Contrast targets

- Main text (`Text` on `Background`): WCAG AA for normal text (>= 4.5:1)
- Status text (`Success`, `Error`, `Warning` on `Background`): WCAG AA for normal text (>= 4.5:1)
- Accent text in content (`AccentText` on `Background`): WCAG AA for normal text (>= 4.5:1)

Contrast checks are covered by tests in `src/Relego.Tests/Tui/BrandColorsTests.cs`. The web UI is checked end to end instead: `src/relego.web/tests/a11y.spec.ts` runs axe-core over every route in both themes at desktop and mobile widths and fails on any contrast violation.
