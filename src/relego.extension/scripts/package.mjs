#!/usr/bin/env node
/**
 * Packages the browser extension for release.
 *
 * Two artifacts are produced, both ready to upload for signing (Chrome Web Store and
 * addons.mozilla.org):
 *
 *   release/relego-extension-<version>-chrome.zip
 *   release/relego-extension-<version>-firefox.zip
 *
 * Chrome keeps the Manifest V3 module service worker built by `vite.config.ts`. Firefox has no
 * service worker in Manifest V3, so `vite.config.firefox.ts` rebuilds the background as a classic
 * event page and this script swaps in a manifest that points at it and carries the AMO identity.
 *
 * The version comes from `RelegoExtensionVersion` in `src/PackageVersions.props`, which is the
 * single source of truth shared with the version-bump check and the release tagging workflow.
 */
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, posix } from "node:path";
import { fileURLToPath } from "node:url";
import JSZip from "jszip";

const extensionDir = join(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = join(extensionDir, "..", "..");
const distDir = join(extensionDir, "dist");
const firefoxBackgroundDir = join(extensionDir, "dist-firefox");
const releaseDir = join(extensionDir, "release");

/** Mozilla requires an explicit add-on id to sign a Manifest V3 package; AMO assigns none. */
const AMO_ID = "relego@relego.dev";

/** First desktop Firefox release with built-in data collection consent. */
const FIREFOX_MIN_VERSION = "140.0";

/** Files the Firefox package supplies itself instead of taking them from the Chrome build. */
const FIREFOX_REPLACED_FILES = new Set(["manifest.json", "background.js"]);

function readExtensionVersion() {
  const propsPath = join(repoRoot, "src", "PackageVersions.props");
  const props = readFileSync(propsPath, "utf8");
  const match = /<RelegoExtensionVersion>([^<]+)<\/RelegoExtensionVersion>/.exec(props);

  if (match === null || match[1] === undefined) {
    throw new Error(`RelegoExtensionVersion is missing from ${propsPath}`);
  }

  return match[1].trim();
}

function readBaseManifest() {
  return JSON.parse(readFileSync(join(extensionDir, "manifest.json"), "utf8"));
}

function chromeManifest(base, version) {
  return { ...base, version };
}

function firefoxManifest(base, version) {
  const { background, ...rest } = base;

  return {
    ...rest,
    version,
    background: { scripts: [background.service_worker] },
    browser_specific_settings: {
      gecko: {
        id: AMO_ID,
        strict_min_version: FIREFOX_MIN_VERSION,
        data_collection_permissions: {
          required: ["authenticationInfo", "websiteContent"],
        },
      },
    },
  };
}

function collectFiles(dir, prefix = "") {
  const found = [];

  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      found.push(...collectFiles(join(dir, entry.name), posix.join(prefix, entry.name)));
      continue;
    }

    found.push(posix.join(prefix, entry.name));
  }

  return found;
}

function addFiles(zip, dir, files) {
  for (const file of files) {
    zip.file(file, readFileSync(join(dir, ...file.split("/"))));
  }
}

function run(command, args) {
  const executable = process.platform === "win32" ? `${command}.cmd` : command;
  execFileSync(executable, args, { cwd: extensionDir, stdio: "inherit" });
}

async function writeZip(path, addContents) {
  const zip = new JSZip();
  await addContents(zip);
  writeFileSync(path, await zip.generateAsync({ type: "nodebuffer", compression: "DEFLATE" }));
  return path;
}

async function main() {
  const version = readExtensionVersion();
  const base = readBaseManifest();

  run("npm", ["run", "build"]);
  run("npm", ["run", "build:firefox"]);

  rmSync(releaseDir, { recursive: true, force: true });
  mkdirSync(releaseDir, { recursive: true });

  const distFiles = collectFiles(distDir).filter((file) => file !== "manifest.json");

  const chromePath = await writeZip(
    join(releaseDir, `relego-extension-${version}-chrome.zip`),
    async (zip) => {
      addFiles(zip, distDir, distFiles);
      zip.file("manifest.json", `${JSON.stringify(chromeManifest(base, version), null, 2)}\n`);
    },
  );

  const firefoxPath = await writeZip(
    join(releaseDir, `relego-extension-${version}-firefox.zip`),
    async (zip) => {
      addFiles(
        zip,
        distDir,
        distFiles.filter((file) => !FIREFOX_REPLACED_FILES.has(file)),
      );
      addFiles(zip, firefoxBackgroundDir, ["background.js"]);
      zip.file("manifest.json", `${JSON.stringify(firefoxManifest(base, version), null, 2)}\n`);
    },
  );

  console.log(`Packaged extension ${version}:`);
  console.log(`  ${chromePath}`);
  console.log(`  ${firefoxPath}`);
}

await main();
