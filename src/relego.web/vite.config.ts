import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "dist",
    sourcemap: false,
  },
  server: {
    port: 5173,
    proxy: {
      "/books": "http://localhost:8080",
      "/exclusions": "http://localhost:8080",
      "/highlights": "http://localhost:8080",
      "/imports": "http://localhost:8080",
      "/recaps": "http://localhost:8080",
      "/settings": "http://localhost:8080",
      "/status": "http://localhost:8080",
    },
  },
});
