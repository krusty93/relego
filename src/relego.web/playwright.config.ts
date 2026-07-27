import { defineConfig, devices } from "@playwright/test";
import { rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

// The port is overridable so the suite can run alongside an existing server.
const API_PORT = Number(process.env.RELEGO_E2E_API_PORT ?? 8080);
export const API_URL = `http://localhost:${API_PORT}`;
const SERVER_PROJECT = resolve("../Relego.Server/Relego.Server.csproj");
const PUBLISH_DIRECTORY = join(tmpdir(), "relego-web-e2e-publish");
const SERVER_DLL = join(PUBLISH_DIRECTORY, "Relego.Server.dll");

// A throwaway database per run, removed here so it is gone before the server boots.
const DB_PATH = join(tmpdir(), "relego-web-e2e.db");
for (const suffix of ["", "-wal", "-shm"]) {
  rmSync(DB_PATH + suffix, { force: true });
}
export default defineConfig({
  testDir: "./tests",
  globalSetup: "./tests/global-setup.ts",
  // The suite drives one shared server, so ordering and isolation depend on serial execution.
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? "github" : "list",
  timeout: 60_000,
  use: {
    baseURL: `${API_URL}/`,
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "desktop",
      use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 950 } },
    },
    {
      name: "mobile",
      testMatch: /a11y\.spec\.ts/,
      use: { ...devices["Pixel 7"] },
    },
  ],
  webServer: {
    command: `dotnet publish "${SERVER_PROJECT}" --configuration Release --output "${PUBLISH_DIRECTORY}" && dotnet "${SERVER_DLL}"`,
    url: `${API_URL}/healthz/startup`,
    reuseExistingServer: !process.env.CI,
    timeout: 240_000,
    stdout: "pipe",
    env: {
      ASPNETCORE_URLS: API_URL,
      ASPNETCORE_CONTENTROOT: PUBLISH_DIRECTORY,
      RELEGO_DB_PATH: DB_PATH,
      SMTP_HOST: "smtp.example.com",
      SMTP_PORT: "587",
      SMTP_FROM_ADDRESS: "noreply@relego.local",
      KINDLE_EMAIL: "reader@kindle.com",
    },
  },
});
