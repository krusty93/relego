import { join } from "node:path";
import { defineConfig } from "vite";

/**
 * Firefox build of the background script.
 *
 * Manifest V3 in Firefox has no service worker: the background is a classic event page, so it must
 * be one self-contained file with no ES module syntax and no imports at runtime. The popup is
 * identical across browsers and is taken from the Chrome build (`vite.config.ts`).
 */
export default defineConfig({
  build: {
    outDir: "dist-firefox",
    emptyOutDir: true,
    target: "es2022",
    sourcemap: false,
    lib: {
      entry: join(import.meta.dirname, "src", "background.ts"),
      formats: ["iife"],
      name: "RelegoBackground",
      fileName: () => "background.js",
    },
  },
});
