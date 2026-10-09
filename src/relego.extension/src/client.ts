/**
 * Client for the Relego server's cloud-sync channel.
 *
 * The concrete calls (pairing, heartbeat, job lifecycle) are added with the synchronization task;
 * this module exists so the transport seam is explicit and typed.
 */

/** Configuration required to talk to the user's Relego server. */
export interface RelegoClientConfig {
  /** Base URL of the user's Relego server, without a trailing slash. */
  readonly baseUrl: string;
  /** Pairing token issued by the server, sent as a bearer token. */
  readonly token: string;
}
