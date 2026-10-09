import { copyFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import { defineConfig, type Plugin } from "vite";

/**
 * Copies the manifest into the build output so `dist/` can be loaded unpacked.
 */
function copyManifest(): Plugin {
  return {
    name: "relego-copy-manifest",
    closeBundle() {
      const manifest = join(import.meta.dirname, "manifest.json");
      const target = join(import.meta.dirname, "dist", "manifest.json");

      if (existsSync(manifest)) {
        copyFileSync(manifest, target);
      }
    },
  };
}

export default defineConfig({
  plugins: [copyManifest()],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    target: "es2022",
    modulePreload: false,
    sourcemap: false,
    rollupOptions: {
      input: {
        background: join(import.meta.dirname, "src", "background.ts"),
      },
      output: {
        entryFileNames: "[name].js",
        chunkFileNames: "[name].js",
        assetFileNames: "[name][extname]",
      },
    },
  },
});
