/**
 * Runtime configuration. The container entrypoint writes `/config.js`, which sets
 * `window.__RELEGO__`, so a single static build can point at any server without a rebuild.
 */
declare global {
  interface Window {
    __RELEGO__?: { apiUrl?: string };
  }
}

function readApiUrl(): string {
  const injected = window.__RELEGO__?.apiUrl?.trim();
  if (injected && injected !== "__RELEGO_API_URL__") {
    return injected.replace(/\/$/, "");
  }

  // Dev fallback: the server's default local port.
  return "http://localhost:8080";
}

export const API_URL = readApiUrl();
