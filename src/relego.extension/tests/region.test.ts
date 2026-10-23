/**
 * Region picker tests.
 *
 * The picker is the only way a region is ever chosen, so these tests pin the four things that make
 * it safe: it offers exactly the bundled allowlist and nothing host-shaped, host access is asked
 * for only for the host it is about to read, the region the user leaves is released, and a refusal
 * — of the prompt or of the server call — leaves the connection on the region it already read.
 */

import { describe, expect, it } from "vitest";

import { notebookOrigin, serverOriginPattern, staleNotebookOrigins } from "../src/permissions";
import type { HostPermissions } from "../src/permissions";
import { REGIONS, resolveRegion } from "../src/profile";
import type { KindleRegion } from "../src/profile";
import { chooseRegion, regionOptions } from "../src/region-change";
import type { RegionChoicePorts } from "../src/region-change";

const SERVER = "http://relego.test";

/** The origin pattern the server address grants. */
const SERVER_ORIGIN = "http://relego.test/*";

const US_NOTEBOOK = "https://read.amazon.com/*";
const DE_NOTEBOOK = "https://lesen.amazon.de/*";

function region(id: string): KindleRegion {
  const resolved = resolveRegion(id);
  if (resolved === null) throw new Error(`The ${id} region must be part of the bundled profile.`);

  return resolved;
}

interface FakePermissions {
  readonly port: HostPermissions;
  readonly requests: readonly (readonly string[])[];
  readonly removals: readonly (readonly string[])[];
  readonly granted: ReadonlySet<string>;
  denyRequests: () => void;
}

/** Host permissions that grant what is asked unless a test denies it, and record every call. */
function createPermissions(initial: readonly string[] = []): FakePermissions {
  const granted = new Set(initial);
  const requests: string[][] = [];
  const removals: string[][] = [];
  let allow = true;

  const port: HostPermissions = {
    contains: (origins) => Promise.resolve(origins.every((origin) => granted.has(origin))),
    request: (origins) => {
      requests.push([...origins]);
      if (!allow) return Promise.resolve(false);

      origins.forEach((origin) => granted.add(origin));

      return Promise.resolve(true);
    },
    remove: (origins) => {
      removals.push([...origins]);
      origins.forEach((origin) => granted.delete(origin));

      return Promise.resolve();
    },
    granted: () => Promise.resolve([...granted]),
  };

  return {
    port,
    requests,
    removals,
    granted,
    denyRequests: () => {
      allow = false;
    },
  };
}

/** How the fake server answers the calls the region flow makes. */
interface ServerBehaviour {
  readonly intervalMinutes?: number;
  readonly pendingCommand?: string;
  /** Status to reject with; `null` rejects without a status, like a network failure. */
  readonly failWith?: number | null;
}

interface FakeServer {
  readonly ports: RegionChoicePorts;
  readonly paired: readonly string[];
  readonly switched: readonly string[];
}

/** A server that accepts the choice unless the test makes it fail. */
function createServer(permissions: HostPermissions, behaviour: ServerBehaviour = {}): FakeServer {
  const paired: string[] = [];
  const switched: string[] = [];

  const reject = (): Promise<never> =>
    behaviour.failWith === null
      ? Promise.reject(new Error("network down"))
      : Promise.reject(Object.assign(new Error("server refused"), { status: behaviour.failWith }));

  return {
    paired,
    switched,
    ports: {
      permissions,
      pair: (regionId) => {
        paired.push(regionId);

        return behaviour.failWith === undefined
          ? Promise.resolve(behaviour.intervalMinutes ?? 60)
          : reject();
      },
      setRegion: (regionId) => {
        switched.push(regionId);

        return behaviour.failWith === undefined
          ? Promise.resolve(behaviour.pendingCommand ?? "none")
          : reject();
      },
    },
  };
}

describe("region options", () => {
  it("offers exactly the bundled allowlist, in allowlist order", () => {
    const options = regionOptions();

    expect(options).toHaveLength(8);
    expect(options.map((option) => option.value)).toEqual(REGIONS.map((option) => option.id));
  });

  it("labels every option with the region and the host it reads", () => {
    const options = regionOptions();

    expect(options[0]).toEqual({
      value: "us",
      label: "United States — read.amazon.com",
      host: "read.amazon.com",
    });
    expect(options[3]).toEqual({
      value: "de",
      label: "Germany — lesen.amazon.de",
      host: "lesen.amazon.de",
    });
  });

  it("never offers a host as a value, so no host can be selected", () => {
    const values = regionOptions().map((option) => option.value);

    expect(values).toEqual(["us", "ca", "uk", "de", "fr", "it", "es", "br"]);
    expect(values.some((value) => value.includes(".") || value.includes("/"))).toBe(false);
  });
});

describe("origin patterns", () => {
  it("derives the notebook pattern from the allowlist host", () => {
    expect(notebookOrigin(region("de"))).toBe(DE_NOTEBOOK);
  });

  it("accepts only an absolute http(s) server address", () => {
    expect(serverOriginPattern(SERVER)).toBe(SERVER_ORIGIN);
    expect(serverOriginPattern("https://relego.example/relego/")).toBe("https://relego.example/*");
    expect(serverOriginPattern("relego.example")).toBeNull();
    expect(serverOriginPattern("ftp://relego.example")).toBeNull();
    expect(serverOriginPattern("")).toBeNull();
  });

  it("releases only allowlisted notebook hosts, never another grant", () => {
    const stale = staleNotebookOrigins([US_NOTEBOOK, SERVER_ORIGIN, "https://unrelated.test/*"], region("de"));

    expect(stale).toEqual([US_NOTEBOOK]);
  });

  it("releases nothing when the selected region is the only notebook host granted", () => {
    expect(staleNotebookOrigins([DE_NOTEBOOK, SERVER_ORIGIN], region("de"))).toEqual([]);
  });
});

describe("choosing a region", () => {
  it("pairs a first connection with the server and the notebook host", async () => {
    const permissions = createPermissions();
    const server = createServer(permissions.port, { intervalMinutes: 60 });

    const outcome = await chooseRegion(
      { regionId: "de", currentRegionId: null, serverBaseUrl: SERVER },
      server.ports,
    );

    expect(outcome).toEqual({
      kind: "region_selected",
      region: region("de"),
      intervalMinutes: 60,
      pendingCommand: null,
      released: [],
    });
    expect(permissions.requests).toEqual([[SERVER_ORIGIN, DE_NOTEBOOK]]);
    expect(server.paired).toEqual(["de"]);
    expect(server.switched).toEqual([]);
    expect(permissions.removals).toEqual([]);
  });

  it("releases the host of the region the connection is leaving", async () => {
    const permissions = createPermissions([US_NOTEBOOK, SERVER_ORIGIN]);
    const server = createServer(permissions.port, { pendingCommand: "full_resync" });

    const outcome = await chooseRegion(
      { regionId: "de", currentRegionId: "us", serverBaseUrl: null },
      server.ports,
    );

    expect(outcome).toEqual({
      kind: "region_selected",
      region: region("de"),
      intervalMinutes: null,
      pendingCommand: "full_resync",
      released: [US_NOTEBOOK],
    });
    expect(permissions.requests).toEqual([[DE_NOTEBOOK]]);
    expect(permissions.removals).toEqual([[US_NOTEBOOK]]);
    expect(server.switched).toEqual(["de"]);
    expect(server.paired).toEqual([]);
    expect(permissions.granted.has(US_NOTEBOOK)).toBe(false);
    expect(permissions.granted.has(DE_NOTEBOOK)).toBe(true);
    expect(permissions.granted.has(SERVER_ORIGIN)).toBe(true);
  });

  it("leaves the connection untouched when the user denies host access", async () => {
    const permissions = createPermissions([US_NOTEBOOK, SERVER_ORIGIN]);
    permissions.denyRequests();
    const server = createServer(permissions.port);

    const outcome = await chooseRegion(
      { regionId: "de", currentRegionId: "us", serverBaseUrl: null },
      server.ports,
    );

    expect(outcome).toEqual({ kind: "access_denied", region: region("de"), origins: [DE_NOTEBOOK] });
    expect(server.switched).toEqual([]);
    expect(server.paired).toEqual([]);
    expect(permissions.removals).toEqual([]);
    expect(permissions.granted.has(US_NOTEBOOK)).toBe(true);
  });

  it("does not call the server at all when the first pairing is refused", async () => {
    const permissions = createPermissions();
    permissions.denyRequests();
    const server = createServer(permissions.port);

    const outcome = await chooseRegion(
      { regionId: "uk", currentRegionId: null, serverBaseUrl: SERVER },
      server.ports,
    );

    expect(outcome).toEqual({
      kind: "access_denied",
      region: region("uk"),
      origins: [SERVER_ORIGIN, "https://read.amazon.co.uk/*"],
    });
    expect(server.paired).toEqual([]);
    expect(server.switched).toEqual([]);
  });

  it("rejects a host-shaped or unknown id before asking for any access", async () => {
    const permissions = createPermissions();
    const server = createServer(permissions.port);

    for (const regionId of ["read.amazon.de", "https://lesen.amazon.de", "jp", "US", "", "//"]) {
      const outcome = await chooseRegion(
        { regionId, currentRegionId: "us", serverBaseUrl: null },
        server.ports,
      );

      expect(outcome).toEqual({ kind: "unsupported_region", regionId });
    }

    expect(permissions.requests).toEqual([]);
    expect(permissions.removals).toEqual([]);
    expect(server.switched).toEqual([]);
    expect(server.paired).toEqual([]);
  });

  it("resolves padded input to the allowlisted host rather than to what was typed", () => {
    expect(resolveRegion(" de ")?.notebookHost).toBe("lesen.amazon.de");
    expect(regionOptions().map((option) => option.host)).toContain("lesen.amazon.de");
  });

  it("rejects a server address that is not an absolute http(s) URL before asking for access", async () => {
    const permissions = createPermissions();
    const server = createServer(permissions.port);

    const outcome = await chooseRegion(
      { regionId: "us", currentRegionId: null, serverBaseUrl: "localhost:8080" },
      server.ports,
    );

    expect(outcome).toEqual({ kind: "invalid_server_url", serverBaseUrl: "localhost:8080" });
    expect(permissions.requests).toEqual([]);
    expect(server.paired).toEqual([]);
  });

  it("hands the access back when the server refuses the change", async () => {
    const permissions = createPermissions([US_NOTEBOOK, SERVER_ORIGIN]);
    const server = createServer(permissions.port, { failWith: 401 });

    const outcome = await chooseRegion(
      { regionId: "de", currentRegionId: "us", serverBaseUrl: null },
      server.ports,
    );

    expect(outcome).toEqual({ kind: "failed", region: region("de"), status: 401 });
    expect(server.switched).toEqual(["de"]);
    expect(permissions.removals).toEqual([[DE_NOTEBOOK]]);
    expect(permissions.granted.has(US_NOTEBOOK)).toBe(true);
    expect(permissions.granted.has(DE_NOTEBOOK)).toBe(false);
  });

  it("reports a server call that failed without a status", async () => {
    const permissions = createPermissions([US_NOTEBOOK, SERVER_ORIGIN]);
    const server = createServer(permissions.port, { failWith: null });

    const outcome = await chooseRegion(
      { regionId: "fr", currentRegionId: "us", serverBaseUrl: null },
      server.ports,
    );

    expect(outcome).toEqual({ kind: "failed", region: region("fr"), status: null });
  });

  it("never moves the region when the pairing call fails", async () => {
    const permissions = createPermissions();
    const server = createServer(permissions.port, { failWith: 500 });

    const outcome = await chooseRegion(
      { regionId: "es", currentRegionId: null, serverBaseUrl: SERVER },
      server.ports,
    );

    expect(outcome).toEqual({ kind: "failed", region: region("es"), status: 500 });
    expect(server.paired).toEqual(["es"]);
    expect(server.switched).toEqual([]);
    expect(permissions.removals).toEqual([["https://leer.amazon.es/*"]]);
  });
});

describe("re-selecting the region already in use", () => {
  it("changes nothing and asks the server for nothing", async () => {
    const permissions = createPermissions([DE_NOTEBOOK, SERVER_ORIGIN]);
    const server = createServer(permissions.port);

    const outcome = await chooseRegion(
      { regionId: "de", currentRegionId: "de", serverBaseUrl: null },
      server.ports,
    );

    expect(outcome).toEqual({ kind: "already_selected", region: region("de") });
    expect(permissions.requests).toEqual([]);
    expect(permissions.removals).toEqual([]);
    expect(server.switched).toEqual([]);
    expect(server.paired).toEqual([]);
  });

  it("repairs a missing grant without switching the region", async () => {
    const permissions = createPermissions([SERVER_ORIGIN]);
    const server = createServer(permissions.port);

    const outcome = await chooseRegion(
      { regionId: "de", currentRegionId: "de", serverBaseUrl: null },
      server.ports,
    );

    expect(outcome).toEqual({ kind: "already_selected", region: region("de") });
    expect(permissions.requests).toEqual([[DE_NOTEBOOK]]);
    expect(permissions.granted.has(DE_NOTEBOOK)).toBe(true);
    expect(server.switched).toEqual([]);
  });

  it("says access was denied when the repair is refused", async () => {
    const permissions = createPermissions([SERVER_ORIGIN]);
    permissions.denyRequests();
    const server = createServer(permissions.port);

    const outcome = await chooseRegion(
      { regionId: "de", currentRegionId: "de", serverBaseUrl: null },
      server.ports,
    );

    expect(outcome).toEqual({ kind: "access_denied", region: region("de"), origins: [DE_NOTEBOOK] });
    expect(server.switched).toEqual([]);
  });
});
