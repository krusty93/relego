/**
 * Client for the Relego server's cloud-sync channel.
 *
 * Provides the typed transport (`getJson`/`postJson` with the bearer token) plus the pairing,
 * heartbeat and job-lifecycle calls the popup and the service worker need.
 */

import { PROFILE_VERSION } from "./profile";

/** Provider id of the Kindle cloud sync provider. */
export const PROVIDER_ID = "kindle-cloud";

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

/** A book the server has already imported, used to skip unchanged books. */
export interface RelegoKnownBook {
  /** Normalized book key shared with the server. */
  readonly key: string;
  /** Highlight count the server last stored for that key. */
  readonly highlightCount: number;
}

/** Result of starting a sync job. */
export interface RelegoJobStart {
  /** Identifier of the job to report batches and progress against. */
  readonly jobId: number;
  /** Fingerprint of already imported books; empty for routine (incremental) jobs. */
  readonly knownBooks: readonly RelegoKnownBook[];
}

/** A highlight as uploaded to the server. */
export interface RelegoBatchHighlight {
  /** Stable id derived from the notebook markup, when present. */
  readonly externalId: string | null;
  /** Highlight text. */
  readonly text: string;
  /** Attached note, when present. */
  readonly note: string | null;
  /** Reader location, when present. */
  readonly location: string | null;
  /** Highlight color, when present. */
  readonly color: string | null;
}

/** A book as uploaded to the server. */
export interface RelegoBatchBook {
  /** Stable book key from the notebook library. */
  readonly externalKey: string | null;
  /** Book title. */
  readonly title: string;
  /** Book author, when present. */
  readonly author: string | null;
  /** Whether the book hit the publisher export limit. */
  readonly truncated: boolean;
  /** Highlights read from the book page. */
  readonly highlights: readonly RelegoBatchHighlight[];
}

/** One upload batch. */
export interface RelegoBatch {
  /** Zero-based batch index; replays with the same index are idempotent. */
  readonly batchIndex: number;
  /** Books carried by this batch. */
  readonly books: readonly RelegoBatchBook[];
}

/** Counts returned for an uploaded batch. */
export interface RelegoBatchResult {
  /** Index echoed back by the server. */
  readonly batchIndex: number;
  /** Whether the server recognised this batch as a replay. */
  readonly replayed: boolean;
  /** Highlights inserted by this batch. */
  readonly newHighlights: number;
  /** Highlights already known to the server. */
  readonly duplicateHighlights: number;
  /** Books inserted by this batch. */
  readonly newBooks: number;
  /** Authors inserted by this batch. */
  readonly newAuthors: number;
}

/** Progress report for a running job. */
export interface RelegoProgress {
  /** One of `discovering`, `reading`, `uploading`, `done`. */
  readonly phase: string;
  /** Books discovered in the notebook, once known. */
  readonly booksTotal: number | null;
  /** Books fully read, once known. */
  readonly booksDone: number | null;
}

/** Explicit completion counts; `booksTotal` must equal `booksDone`. */
export interface RelegoJobCompletion {
  /** Books discovered in the notebook. */
  readonly booksTotal: number;
  /** Books fully read. */
  readonly booksDone: number;
  /** Books that hit the publisher export limit. */
  readonly truncatedBooks: number;
}

/** Failure report for a job. */
export interface RelegoJobFailure {
  /** One of `auth_expired`, `provider_contract_changed`, `network`, `server_rejected`, `interrupted`. */
  readonly code: string;
  /** Sanitized diagnostics; never page free text. */
  readonly diagnostics?: {
    readonly profileVersion: string;
    readonly check: string;
    readonly counts: Record<string, number>;
  } | null;
}

/** Heartbeat payload reporting liveness and the applied schedule. */
export interface RelegoHeartbeatReport {
  /** Parse profile version the extension is running. */
  readonly profileVersion: string;
  /** Sync frequency currently applied to the local alarm. */
  readonly appliedIntervalMinutes: number;
  /** ISO timestamp of the next scheduled sync, or `null` when unknown. */
  readonly nextSyncDueAt: string | null;
}

/** Heartbeat response carrying the stored schedule and any user-requested command. */
export interface RelegoHeartbeat {
  /** `none`, `sync` or `full_resync`; consumed on read. */
  readonly command: string;
  /** Region the server stored for this connection. */
  readonly region: RelegoRegion | null;
  /** Sync frequency the extension must apply. */
  readonly intervalMinutes: number;
  /** Fixed heartbeat cadence in seconds. */
  readonly heartbeatSeconds: number;
}

/** Outcome of exchanging a pairing token for an active connection. */
export interface RelegoPairing {
  /** Provider id echoed by the server. */
  readonly providerId: string;
  /** Connection state after pairing. */
  readonly state: string;
  /** Sync frequency the extension must apply. */
  readonly intervalMinutes: number;
  /** Frequencies the server accepts. */
  readonly allowedIntervalMinutes: readonly number[];
  /** Region the server stored for this connection. */
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

  /** Activates the connection using the stored pairing token as the bearer credential. */
  pair(regionId: string): Promise<RelegoPairing> {
    return this.postJson<RelegoPairing>(`/sync/${PROVIDER_ID}/pair`, {
      profileVersion: PROFILE_VERSION,
      regionId,
    });
  }

  /** Starts a sync job; a replay of the same idempotency key returns the same job. */
  startJob(request: {
    readonly idempotencyKey: string;
    readonly mode: string;
    readonly trigger: string;
  }): Promise<RelegoJobStart> {
    return this.postJson<RelegoJobStart>(`/sync/${PROVIDER_ID}/jobs`, request);
  }

  /** Uploads one batch; replays with the same `batchIndex` are idempotent. */
  postBatch(jobId: number, batch: RelegoBatch): Promise<RelegoBatchResult> {
    return this.postJson<RelegoBatchResult>(
      `/sync/${PROVIDER_ID}/jobs/${jobId}/batches`,
      batch,
    );
  }

  /** Reports job progress so the server can mark stalled runs as interrupted. */
  reportProgress(jobId: number, progress: RelegoProgress): Promise<void> {
    return this.postJson<void>(`/sync/${PROVIDER_ID}/jobs/${jobId}/progress`, progress);
  }

  /** Completes a job; the server rejects mismatched book counts. */
  completeJob(jobId: number, completion: RelegoJobCompletion): Promise<void> {
    return this.postJson<void>(`/sync/${PROVIDER_ID}/jobs/${jobId}/complete`, completion);
  }

  /** Reports a failed job with a recovery code and sanitized diagnostics. */
  failJob(jobId: number, failure: RelegoJobFailure): Promise<void> {
    return this.postJson<void>(`/sync/${PROVIDER_ID}/jobs/${jobId}/fail`, failure);
  }

  /** Reports liveness and reads the stored schedule and any pending command. */
  heartbeat(report: RelegoHeartbeatReport): Promise<RelegoHeartbeat> {
    return this.postJson<RelegoHeartbeat>(`/sync/${PROVIDER_ID}/heartbeat`, report);
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
