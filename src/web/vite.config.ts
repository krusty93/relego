import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In the container the API URL is injected into /config.js by the entrypoint.
// In dev the file's default is used unless RELEGO_API_URL overrides it, which is
// what lets the E2E suite move the server off its default port.
const apiUrl = process.env.RELEGO_API_URL;

export default defineConfig({
  plugins: [
    react(),
    apiUrl
      ? {
          name: "relego-dev-config",
          configureServer(server) {
            server.middlewares.use("/config.js", (_req, res) => {
              res.setHeader("Content-Type", "application/javascript");
              res.end(`window.__RELEGO__ = ${JSON.stringify({ apiUrl })};`);
            });
          },
        }
      : null,
  ],
  build: {
    outDir: "dist",
    sourcemap: false,
  },
  server: {
    port: 5173,
  },
});
