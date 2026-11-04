# Quick Start: Landing Page

**Feature**: 008-landing-page
**Date**: 2026-05-14

---

## Prerequisites

- Node.js 24 installed (`node --version`)
- pnpm installed through Corepack (`corepack enable`, then `pnpm --version`)

The landing page is **completely independent** from the .NET solution. You do not need the .NET SDK to work on it. It is one package of the repository-wide pnpm workspace, so dependencies are installed once from the repository root.

## Project Location

```
src/landing/
├── astro.config.mjs       # Astro configuration
├── package.json            # Node.js dependencies
├── tsconfig.json           # TypeScript configuration
├── public/                 # Static assets (favicon, etc.)
├── assets/                 # Images (hero-kindle.jpg)
├── components/             # Astro components
│   ├── Accordion.astro
│   ├── Button.astro
│   ├── FeatureCard.astro
│   ├── Footer.astro
│   ├── GettingStarted.astro
│   ├── Navbar.astro
│   ├── Section.astro
│   └── ThemeToggle.astro
├── config/
│   └── site.ts             # Site configuration (GitHub URL, etc.)
├── layouts/
│   └── Layout.astro        # Base HTML layout
├── pages/
│   └── index.astro         # The landing page
├── styles/
│   └── global.css          # Global styles + Tailwind
└── tests/                  # Playwright E2E tests
    ├── navigation.spec.ts
    ├── theme.spec.ts
    ├── content-sections.spec.ts
    ├── faq.spec.ts
    └── accessibility.spec.ts
```

## Install & Run

```bash
# Install workspace dependencies from the repository root (once)
pnpm install

# Navigate to the landing page project
cd src/landing

# Start dev server (http://localhost:4321)
pnpm run dev
```

## Build for Production

```bash
cd src/landing

# Build static site
pnpm run build

# Preview the production build locally
pnpm run preview
```

The built site is output to `src/landing/dist/`.

## Run E2E Tests

```bash
cd src/landing

# Install Playwright browsers (first time only)
pnpm exec playwright install --with-deps chromium

# Run all tests
pnpm exec playwright test

# Run a specific test file
pnpm exec playwright test tests/faq.spec.ts

# Run tests with UI mode
pnpm exec playwright test --ui
```

Tests run against the Astro dev server, which Playwright starts automatically via the `webServer` config in `playwright.config.ts`.

## Key Configuration

### Site Config (`config/site.ts`)

Update the GitHub URL and other site-wide settings here:

```typescript
export const siteConfig = {
  name: "Relego",
  logotype: "relego.",
  tagline: "Revisit your highlights, delivered to your Kindle. For free.",
  githubUrl: "https://github.com/Krusty93/relego",
  docsUrl: "https://github.com/Krusty93/relego/blob/main/README.md",
  license: "MIT",
  licenseUrl: "https://github.com/Krusty93/relego/blob/main/LICENSE",
  contributingUrl: "https://github.com/Krusty93/relego/blob/main/CONTRIBUTING.md",
} as const;
```

### Astro Config (`astro.config.mjs`)

The `base` and `site` settings control the GitHub Pages deployment path:

```javascript
export default defineConfig({
  site: 'https://krusty93.github.io',
  base: '/sunny-sunday/',
  // ...
});
```

## Deployment

The landing page deploys to GitHub Pages via a dedicated CI workflow (`.github/workflows/landing-page.yml`). It triggers on pushes to `main` that modify files under `src/landing/`.
