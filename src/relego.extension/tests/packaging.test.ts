import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import JSZip from "jszip";
import { beforeAll, describe, expect, it } from "vitest";

const extensionDir = join(fileURLToPath(import.meta.url), "..", "..");
const repoRoot = join(extensionDir, "..", "..");
const releaseDir = join(extensionDir, "release");

function expectedVersion(): string {
  const props = readFileSync(join(repoRoot, "src", "PackageVersions.props"), "utf8");
  const match = /<RelegoExtensionVersion>([^<]+)<\/RelegoExtensionVersion>/.exec(props);

  if (match?.[1] === undefined) {
    throw new Error("RelegoExtensionVersion is missing from src/PackageVersions.props");
  }

  return match[1].trim();
}

async function readPackage(browser: "chrome" | "firefox") {
  const path = join(releaseDir, `relego-extension-${expectedVersion()}-${browser}.zip`);
  const zip = await JSZip.loadAsync(readFileSync(path));
  const manifest = JSON.parse((await zip.file("manifest.json")!.async("string")) as string);

  return { zip, manifest };
}

describe("extension release packaging", () => {
  beforeAll(() => {
    const npm = process.platform === "win32" ? "npm.cmd" : "npm";
    execFileSync(npm, ["run", "package"], { cwd: extensionDir, stdio: "inherit" });
  }, 600_000);

  it("stamps both packages with the centralized extension version", async () => {
    const chrome = await readPackage("chrome");
    const firefox = await readPackage("firefox");

    expect(chrome.manifest.version).toBe(expectedVersion());
    expect(firefox.manifest.version).toBe(expectedVersion());
  });

  it("grants no host at install in either package", async () => {
    for (const browser of ["chrome", "firefox"] as const) {
      const { manifest } = await readPackage(browser);

      expect(manifest.host_permissions).toEqual([]);
      expect(manifest.optional_host_permissions).toEqual(["http://*/*", "https://*/*"]);
      expect(manifest.permissions).toEqual(["storage", "alarms"]);
    }
  });

  it("keeps the Chrome package on a module service worker", async () => {
    const { zip, manifest } = await readPackage("chrome");

    expect(manifest.background).toEqual({ service_worker: "background.js", type: "module" });
    expect(manifest.browser_specific_settings).toBeUndefined();
    expect(zip.file("background.js")).not.toBeNull();
    expect(zip.file("popup.html")).not.toBeNull();
    expect(zip.file("popup.js")).not.toBeNull();
    expect(zip.file("popup.css")).not.toBeNull();
  });

  it("ships Firefox a classic event page with the AMO identity", async () => {
    const { zip, manifest } = await readPackage("firefox");

    expect(manifest.background).toEqual({ scripts: ["background.js"] });
    expect(manifest.browser_specific_settings).toEqual({
      gecko: { id: "relego@relego.dev", strict_min_version: "128.0" },
    });

    const background = (await zip.file("background.js")!.async("string")) as string;

    expect(background).not.toMatch(/^(?:import|export)\b/m);
    expect(zip.file("popup.html")).not.toBeNull();
  });
});
