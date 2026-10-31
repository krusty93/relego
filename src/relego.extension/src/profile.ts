/**
 * Parse profile mirrored between the extension and the Relego server.
 *
 * `REGIONS` must stay identical to `ParseProfile.KindleRegions` on the server; the region tests
 * assert the parity. The notebook host is never user-supplied: it is always derived from this
 * allowlist, and a host-shaped region id is rejected before any navigation happens.
 */

/** Version of the notebook parse profile mirrored by this extension build. */
export const PROFILE_VERSION = "2";

/**
 * Sync frequencies the server accepts, in minutes. Mirrors
 * `KindleCloudProvider.AllowedMinutesList`; anything else is rejected with 422.
 */
export const ALLOWED_INTERVAL_MINUTES: readonly number[] = [15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440];

/** Sync frequency applied until the user chooses another one. */
export const DEFAULT_INTERVAL_MINUTES = 360;

/** Fixed heartbeat cadence in seconds; never derived from the sync frequency. */
export const HEARTBEAT_SECONDS = 60;

/**
 * Clamps a requested frequency to the allowlist.
 *
 * @param minutes Requested frequency in minutes.
 * @returns The requested value when allowed, otherwise the default.
 */
export function clampIntervalMinutes(minutes: number): number {
  return ALLOWED_INTERVAL_MINUTES.includes(minutes) ? minutes : DEFAULT_INTERVAL_MINUTES;
}

/** A validated Amazon Kindle notebook region. */
export interface KindleRegion {
  /** Stable region id shared with the server (for example `us`). */
  readonly id: string;
  /** Human-readable region name. */
  readonly displayName: string;
  /** Official Kindle notebook host for the region. */
  readonly notebookHost: string;
  /** Hosts that indicate Amazon sign-in for the region. */
  readonly signInHostPatterns: readonly string[];
}

/** Regions bundled with this extension build. */
export const REGIONS: readonly KindleRegion[] = [
  {
    id: "us",
    displayName: "United States",
    notebookHost: "read.amazon.com",
    signInHostPatterns: ["www.amazon.com"],
  },
  {
    id: "ca",
    displayName: "Canada",
    notebookHost: "read.amazon.ca",
    signInHostPatterns: ["www.amazon.ca"],
  },
  {
    id: "uk",
    displayName: "United Kingdom",
    notebookHost: "read.amazon.co.uk",
    signInHostPatterns: ["www.amazon.co.uk"],
  },
  {
    id: "de",
    displayName: "Germany",
    notebookHost: "lesen.amazon.de",
    signInHostPatterns: ["www.amazon.de"],
  },
  {
    id: "fr",
    displayName: "France",
    notebookHost: "lire.amazon.fr",
    signInHostPatterns: ["www.amazon.fr"],
  },
  {
    id: "it",
    displayName: "Italy",
    notebookHost: "leggi.amazon.it",
    signInHostPatterns: ["www.amazon.it"],
  },
  {
    id: "es",
    displayName: "Spain",
    notebookHost: "leer.amazon.es",
    signInHostPatterns: ["www.amazon.es"],
  },
  {
    id: "br",
    displayName: "Brazil",
    notebookHost: "ler.amazon.com.br",
    signInHostPatterns: ["www.amazon.com.br"],
  },
];

/**
 * Disclosure shown wherever the supported regions are listed. Mirrors
 * `KindleCloudProvider.RegionDisclosureText`, the text the server sends with its own region list.
 */
export const REGION_DISCLOSURE =
  "Only these validated hosts are supported; other Amazon regions are unsupported until validated.";

/**
 * Resolves an allowlisted region by id.
 *
 * Host-shaped input (`https://...`, `read.amazon.de`, `a/b`) is rejected so a region can never be
 * turned into an arbitrary navigation target.
 *
 * @param regionId Region id reported by the server or chosen in the popup.
 * @returns The matching region, or `null` when the id is unknown or host-shaped.
 */
export function resolveRegion(regionId: string | null | undefined): KindleRegion | null {
  if (typeof regionId !== "string") return null;

  const trimmed = regionId.trim();
  if (trimmed.length === 0) return null;
  if (trimmed.includes("://") || trimmed.includes(".") || trimmed.includes("/")) return null;

  return REGIONS.find((region) => region.id === trimmed) ?? null;
}

/** Absolute notebook URL for a region. */
export function notebookUrl(region: KindleRegion): string {
  return `https://${region.notebookHost}/notebook`;
}

/**
 * Absolute URL of a single book's highlight page.
 *
 * The library list carries the book key, so the book page is always built from the allowlisted
 * host plus that key; a book key is never treated as a URL.
 *
 * @param region Region the notebook is read from.
 * @param externalKey Book key taken from the library list.
 * @returns The book page URL.
 */
export function notebookBookUrl(region: KindleRegion, externalKey: string): string {
  return `${notebookUrl(region)}?bookKey=${encodeURIComponent(externalKey)}`;
}

/** Reports whether a URL is the Amazon sign-in surface of the given region. */
export function isSignInUrl(region: KindleRegion, url: string): boolean {
  let parsed: URL;

  try {
    parsed = new URL(url);
  } catch {
    return false;
  }

  return region.signInHostPatterns.some((host) => parsed.hostname === host);
}

/**
 * Normalizes a book title into a fallback notebook lookup key when no provider key is present.
 * This is not a stable provider identity and must never be used to skip a book via `knownBooks`.
 *
 * @param title Raw book title, or `null`.
 * @returns The normalized key, or an empty string when there is no usable title.
 */
export function normalizeBookKey(title: string | null | undefined): string {
  if (typeof title !== "string") return "";

  return title.replace(/\s+/g, " ").trim().toLowerCase();
}
