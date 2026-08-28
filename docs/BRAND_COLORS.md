# Relego Brand Palette

This document defines Relego's stable identity colors for assets such as the landing page and email. It is not a duplicate token reference for product interfaces.

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

## Implementation

Implementations derive their own semantic tokens from the canonical palette:

- `src/landing/styles/global.css` uses the canonical palette for the public site.
- `src/relego.web/src/styles/tokens.css` contains the product UI's derived tokens. Its light-mode accent variants preserve contrast because the raw `#b56b39` accent is not suitable for normal-sized text or white button text.
- `src/Relego.Core/Branding/BrandColors.cs` contains the CLI mapping.

## Contrast targets

- Main and status text meet WCAG AA for normal text (>= 4.5:1).
- Accent text uses a contrast-safe semantic variant rather than the raw light-mode accent.

The web UI is checked end to end by `src/relego.web/tests/a11y.spec.ts`, which runs axe-core over every route in both themes at desktop and mobile widths.
