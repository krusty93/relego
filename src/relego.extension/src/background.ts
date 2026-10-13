/**
 * Extension service worker (Manifest V3).
 *
 * Registers the routine `kindle-sync` alarm (period = the user-chosen sync frequency), the fixed
 * sixty-second `kindle-heartbeat` alarm, reads the Kindle notebook in the user's own authenticated
 * session and pushes highlights to the user's Relego server.
 *
 * The behavior lands with the synchronization task; this entry point exists so the build produces a
 * loadable `dist/background.js`.
 */

export {};
