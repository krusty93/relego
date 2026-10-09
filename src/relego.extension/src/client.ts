/**
 * Client for the Relego server's cloud-sync channel.
 *
 * The concrete pairing, heartbeat and job-lifecycle calls land with the synchronization task; this
 * module provides the typed transport (`getJson`/`postJson` with the bearer token) and the status
 * call the popup and worker need to drive the UI.
 */

/** Configuration required to talk to the user's Relego server. */
export interface RelegoClientConfig {
  /** Base URL of the user's Relego server, without a trailing slash. */
  readonly baseUrl: string;
  /** Pairing token issued by the server, sent as a bearer token. */
  readonly token: string;
}

/** A validated Amazon region as reported by the server. */
export interface RelegoRegion {
  /** Stable region id shared with the server (for example `us`). */
  readonly id: string;
  /** Human-readable region name. */
  readonly displayName: string;
  /** Official notebook host derived by the server. */
  readonly host: string;
}

/** The subset of the provider status the extension consumes. */
export interface RelegoStatus {
  /** Provider id, for example `kindle-cloud`. */
  readonly providerId: string;
  /** Derived user-visible status. */
  readonly status: string;
  /** The selected region, or `null` until the extension pairs. */
  readonly region: RelegoRegion | null;
}

/** Raised when the Relego server answers with a non-success status. */
export class RelegoApiError extends Error {
  /** HTTP status code returned by the server. */
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "RelegoApiError";
    this.status = status;
  }
}

/** Typed HTTP client for the Relego cloud-sync channel. */
export class RelegoClient {
  private readonly baseUrl: string;
  private readonly token: string;

  constructor(config: RelegoClientConfig) {
    this.baseUrl = config.baseUrl.replace(/\/+$/, "");
    this.token = config.token;
  }

  /** Fetches the provider status. */
  getStatus(providerId: string): Promise<RelegoStatus> {
    return this.getJson<RelegoStatus>(`/sync/${encodeURIComponent(providerId)}`);
  }

  /** Sends a GET request with the bearer token. */
  getJson<T>(path: string): Promise<T> {
    return this.send<T>(path, "GET");
  }

  /** Sends a POST request with a JSON body and the bearer token. */
  postJson<T>(path: string, body?: unknown): Promise<T> {
    return this.send<T>(path, "POST", body);
  }

  private async send<T>(path: string, method: string, body?: unknown): Promise<T> {
    const hasBody = body !== undefined;

    const response = await fetch(`${this.baseUrl}${path}`, {
      method,
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${this.token}`,
        ...(hasBody ? { "Content-Type": "application/json" } : {}),
      },
      ...(hasBody ? { body: JSON.stringify(body) } : {}),
    });

    if (!response.ok) {
      throw new RelegoApiError(`Relego server responded with ${response.status}.`, response.status);
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  }
}
