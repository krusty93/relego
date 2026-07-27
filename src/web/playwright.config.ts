import { defineConfig, devices } from "@playwright/test";
import { rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const API_PORT = 8080;
const WEB_PORT = 5173;
export const API_URL = `http://localhost:${API_PORT}`;

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
    baseURL: `http://localhost:${WEB_PORT}/`,
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
  webServer: [
    {
      command:
        "dotnet run --project ../Relego.Server/Relego.Server.csproj --configuration Release",
      url: `${API_URL}/healthz/startup`,
      reuseExistingServer: !process.env.CI,
      timeout: 240_000,
      stdout: "pipe",
      env: {
        ASPNETCORE_URLS: API_URL,
        RELEGO_DB_PATH: DB_PATH,
        RELEGO_CORS_ORIGINS: `http://localhost:${WEB_PORT}`,
        SMTP_HOST: "smtp.example.com",
        SMTP_PORT: "587",
        SMTP_FROM_ADDRESS: "noreply@relego.local",
        KINDLE_EMAIL: "reader@kindle.com",
      },
    },
    {
      command: `npm run dev -- --port ${WEB_PORT}`,
      url: `http://localhost:${WEB_PORT}/`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
});
