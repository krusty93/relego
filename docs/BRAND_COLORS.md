# Relego Brand Colors

This file is the shared source of truth for color values used across the landing page and the TUI.

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


> **Deprecated:** The TUI is deprecated in favour of the [web UI](http://localhost:8081). It will be removed in a future release. New users should start with the web UI.
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

Contrast checks are covered by tests in `src/Relego.Tests/Tui/BrandColorsTests.cs`.
