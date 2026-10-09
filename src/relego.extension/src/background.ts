/**
 * Extension service worker (Manifest V3).
 *
 * Registers the routine `kindle-sync` alarm (period = the user-chosen sync frequency), the fixed
 * sixty-second `kindle-heartbeat` alarm, reads the Kindle notebook in the user's own authenticated
 * session and pushes highlights to the user's Relego server.
 *
 * Only the configuration seam is wired here; the alarms and sync runner land with the
 * synchronization task so this entry point builds to a loadable `dist/background.js`.
 */

import { RelegoClient } from "./client";
import { readConfig } from "./storage";

/** Reports whether the extension has been paired with a Relego server. */
export async function isConfigured(): Promise<boolean> {
  return (await readConfig()) !== null;
}

/** Creates a client for the stored configuration, or `null` when the extension is not paired. */
export async function createClient(): Promise<RelegoClient | null> {
  const config = await readConfig();
  return config === null ? null : new RelegoClient(config);
}
