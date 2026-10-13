/**
 * Parse profile mirrored between the extension and the Relego server.
 *
 * The region allowlist is populated by the region task; this module exists so both sides can be
 * kept in parity by a single test.
 */

/** Version of the notebook parse profile mirrored by this extension build. */
export const PROFILE_VERSION = "1";

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

/** Regions bundled with this extension build. Populated by the region task. */
export const REGIONS: readonly KindleRegion[] = [];
