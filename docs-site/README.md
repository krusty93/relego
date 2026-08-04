# docs-site

Docusaurus documentation site for Relego.

## Development

```bash
npm run start   # Start local dev server at http://localhost:3000
npm run build   # Build static site to build/
npm run serve   # Serve the built site locally
```

## Adding a page

1. Create a `.md` file in `docs/` (e.g. `docs/configuration.md`)
2. Add frontmatter at the top:
   ```markdown
   ---
   sidebar_position: 2
   ---
   # Page title
   ```
3. The page appears in the sidebar automatically — no manual registration needed.
