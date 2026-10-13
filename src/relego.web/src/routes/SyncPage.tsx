import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router";
import { AsyncSection, EmptyState, ErrorNote, SkeletonRows, Tag } from "../components/ui";
import { syncApi } from "../lib/api";
import { formatCount, formatDateTime, plural } from "../lib/format";
import { useToasts } from "../lib/toasts";
import type {
  SyncDisclosure,
  SyncFailure,
  SyncJob,
  SyncProviderSummary,
  SyncRegion,
  SyncStatusResponse,
} from "../lib/types";

/** The extension checks in on its own cadence, so the page only polls fast while a job runs. */
const POLL_RUNNING_MS = 2_000;
const POLL_IDLE_MS = 30_000;

const PREFERRED_PROVIDER_ID = "kindle-cloud";

const PHASE_LABELS: Record<string, string> = {
  discovering: "Finding your books",
  reading: "Reading highlights",
  uploading: "Sending highlights to Relego",
  done: "Finishing up",
};

const STATE_LABELS: Record<string, string> = {
  not_connected: "Not connected",
  awaiting_pairing: "Waiting for your browser",
  connected: "Connected",
  syncing: "Syncing now",
  completed: "Last sync finished",
  auth_expired: "Amazon sign-in needed",
  failed: "Last sync did not finish",
  browser_not_reporting: "Your browser has not checked in",
  reminder_due: "No recent sync",
};

const STATE_SUMMARIES: Record<string, string> = {
  connected: "Your browser is checking in. The next sync starts on its own.",
  completed: "The last sync finished and the highlights it found are already in your library.",
  syncing: "Your browser is reading your Kindle Notebook and sending what it finds.",
  auth_expired:
    "Amazon ended the session Relego was reading. Sign in again in your browser, then sync.",
  failed: "The last sync stopped before it finished. Nothing partial was imported.",
  browser_not_reporting:
    "Relego has not heard from the extension recently, so syncs are not running. Open the browser where the extension is installed.",
  reminder_due: "It has been a while since your highlights were read.",
};

/**
 * Cloud sync surface: the connect flow, live progress of a running sync, and the explicit
 * outcome of the last one.
 */
export function SyncPage() {
  const providers = useQuery({ queryKey: ["sync", "providers"], queryFn: syncApi.providers });
  const provider =
    providers.data?.find((item) => item.providerId === PREFERRED_PROVIDER_ID) ??
    providers.data?.[0];

  return (
    <section className="view" aria-labelledby="sync-h">
      <div className="view-head">
        <div>
          <h1 id="sync-h">Sync</h1>
          <p>Keep your Kindle Notebook highlights flowing into Relego automatically.</p>
        </div>
      </div>

      <p className="subtle">
        Sync is optional. <Link to="/app/import">Import a highlights file</Link> instead if you would
        rather keep it manual.
      </p>

      <AsyncSection
        isLoading={providers.isLoading}
        error={providers.error}
        onRetry={() => void providers.refetch()}
        skeleton={<SkeletonRows rows={2} />}
      >
        {provider ? (
          <SyncPanel provider={provider} />
        ) : (
          <ErrorNote message="This server exposes no cloud sync provider." />
        )}
      </AsyncSection>
    </section>
  );
}

function SyncPanel({ provider }: { provider: SyncProviderSummary }) {
  const queryClient = useQueryClient();
  const { push } = useToasts();
  const [code, setCode] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<"cancel" | "disconnect" | null>(null);

  const status = useQuery({
    queryKey: ["sync", provider.providerId],
    queryFn: () => syncApi.status(provider.providerId),
    refetchInterval: (query) =>
      query.state.data?.status === "syncing" ? POLL_RUNNING_MS : POLL_IDLE_MS,
  });

  function refresh() {
    void queryClient.invalidateQueries({ queryKey: ["sync", provider.providerId] });
  }

  const connect = useMutation({
    mutationFn: () => syncApi.createConnection(provider.providerId),
    onSuccess: (response) => {
      setCode(response.token);
      setConfirming(null);
      refresh();
      push("One-time code created. Enter it in the Relego browser extension.");
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not start the connection.", {
        tone: "bad",
      }),
  });

  const disconnect = useMutation({
    mutationFn: () => syncApi.disconnect(provider.providerId),
    onSuccess: () => {
      setCode(null);
      setConfirming(null);
      refresh();
      push("Disconnected. Your imported highlights are still here.");
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not disconnect.", { tone: "bad" }),
  });

  // Commands are queued server-side and picked up by the extension's next heartbeat, so success
  // only means "your browser will hear about this", not "the sync already ran".
  const command = useMutation({
    mutationFn: (value: "sync" | "full_resync") => syncApi.command(provider.providerId, value),
    onSuccess: (_response, value) => {
      refresh();
      push(
        value === "full_resync"
          ? "Full resync requested. Your browser re-reads every book on its next check-in."
          : "Sync requested. Your browser starts on its next check-in.",
      );
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not ask your browser to sync.", {
        tone: "bad",
      }),
  });

  async function copy(value: string, label: string) {
    try {
      await navigator.clipboard.writeText(value);
      push(`${label} copied.`);
    } catch {
      push(`Copy by hand: the ${label.toLowerCase()} is shown above.`, { tone: "bad" });
    }
  }

  if (status.isLoading) return <SkeletonRows rows={2} />;

  if (status.error || !status.data) {
    return (
      <ErrorNote
        message={
          status.error instanceof Error ? status.error.message : "Could not read the sync status."
        }
        onRetry={() => void status.refetch()}
      />
    );
  }

  const data = status.data;
  const isFirstRun = data.status === "not_connected";
  const isPairing = data.status === "awaiting_pairing";

  return (
    <>
      {/* Status changes arrive from the extension rather than from a click here, so the change
          is announced instead of silently redrawn. */}
      <p className="sr-only" aria-live="polite">
        {`Sync status: ${STATE_LABELS[data.status] ?? data.status}.`}
      </p>

      {isFirstRun ? null : isPairing ? (
        <PairingPanel
          provider={provider}
          code={code}
          busy={connect.isPending}
          onRegenerate={() => connect.mutate()}
          onCheck={() => void status.refetch()}
          confirming={confirming === "cancel"}
          onAsk={() => setConfirming("cancel")}
          onDismiss={() => setConfirming(null)}
          onCancel={() => disconnect.mutate()}
          cancelling={disconnect.isPending}
          onCopy={copy}
        />
      ) : (
        <StatusPanel
          data={data}
          confirming={confirming === "disconnect"}
          onAsk={() => setConfirming("disconnect")}
          onDismiss={() => setConfirming(null)}
          onDisconnect={() => disconnect.mutate()}
          disconnecting={disconnect.isPending}
          onCommand={(value) => command.mutate(value)}
          pendingCommand={command.isPending ? (command.variables ?? null) : null}
        />
      )}

      <Disclosures disclosure={data.disclosure} prerequisites={data.prerequisites} />

      {isFirstRun ? (
        <div className="section">
          <EmptyState
            title="Connect Kindle cloud"
            action={
              <button
                className="btn btn--primary"
                type="button"
                onClick={() => connect.mutate()}
                aria-busy={connect.isPending || undefined}
              >
                {connect.isPending ? "Starting…" : "Connect Kindle cloud"}
              </button>
            }
          >
            Relego hands your browser a one-time code, then reads your Kindle Notebook inside your
            own Amazon session. The code is shown once, and you can cancel before a browser pairs.
          </EmptyState>
        </div>
      ) : null}
    </>
  );
}

function StatusPanel({
  data,
  confirming,
  onAsk,
  onDismiss,
  onDisconnect,
  disconnecting,
  onCommand,
  pendingCommand,
}: {
  data: SyncStatusResponse;
  confirming: boolean;
  onAsk: () => void;
  onDismiss: () => void;
  onDisconnect: () => void;
  disconnecting: boolean;
  onCommand: (command: "sync" | "full_resync") => void;
  pendingCommand: "sync" | "full_resync" | null;
}) {
  const { status, connection, currentJob, lastCompletedJob, failure } = data;
  const running = status === "syncing" && currentJob !== null;
  const needsSignIn = status === "auth_expired";

  return (
    <div className="panel">
      <header>
        <h2>{STATE_LABELS[status] ?? "Sync"}</h2>
        <p>{STATE_SUMMARIES[status] ?? "Sync runs from the Relego browser extension."}</p>
      </header>

      {needsSignIn ? (
        <AuthExpiredBanner failure={failure} region={connection.region} />
      ) : status === "failed" && failure ? (
        <FailureNote failure={failure} />
      ) : null}

      <SyncActions
        status={status}
        running={running}
        pendingCommand={pendingCommand}
        onCommand={onCommand}
      />

      {running && currentJob ? <JobProgress job={currentJob} /> : null}
      {!running && lastCompletedJob ? <Outcome job={lastCompletedJob} /> : null}

      <dl className="dl">
        <dt>Last synced</dt>
        <dd>
          {lastCompletedJob?.endedAt ? (
            <time dateTime={lastCompletedJob.endedAt}>
              {formatDateTime(lastCompletedJob.endedAt)}
            </time>
          ) : (
            <span className="subtle">Not yet</span>
          )}
        </dd>

        <dt>Last heard from your browser</dt>
        <dd>
          {connection.lastHeartbeatAt ? (
            <time dateTime={connection.lastHeartbeatAt}>
              {formatDateTime(connection.lastHeartbeatAt)}
            </time>
          ) : (
            <span className="subtle">Not yet</span>
          )}
        </dd>
      </dl>

      <div className="confirm disconnect" role="group" aria-label="Disconnect">
        {confirming ? (
          <>
            <span className="confirm-q">
              Disconnect? Highlights already imported stay in your library.
            </span>
            <button
              className="btn btn--danger btn--sm"
              type="button"
              onClick={onDisconnect}
              aria-busy={disconnecting || undefined}
            >
              {disconnecting ? "Disconnecting…" : "Yes, disconnect"}
            </button>
            <button className="btn btn--ghost btn--sm" type="button" onClick={onDismiss}>
              Keep connected
            </button>
          </>
        ) : (
          <button className="btn btn--ghost btn--sm" type="button" onClick={onAsk}>
            Disconnect
          </button>
        )}
      </div>
    </div>
  );
}

/**
 * The explicit next step for the state the user is looking at. Commands are queued server-side and
 * consumed by the extension's next heartbeat, so the row says what will happen rather than
 * pretending the sync already started.
 */
function SyncActions({
  status,
  running,
  pendingCommand,
  onCommand,
}: {
  status: string;
  running: boolean;
  pendingCommand: "sync" | "full_resync" | null;
  onCommand: (command: "sync" | "full_resync") => void;
}) {
  const busySync = pendingCommand === "sync";
  const busyFullResync = pendingCommand === "full_resync";
  const blocked = running || pendingCommand !== null;

  return (
    <div className="panel-actions">
      <button
        className="btn btn--primary"
        type="button"
        disabled={blocked}
        aria-busy={busySync || undefined}
        onClick={() => onCommand("sync")}
      >
        {busySync ? "Asking your browser…" : status === "auth_expired" ? "Retry sync" : "Sync now"}
      </button>

      <button
        className="btn"
        type="button"
        disabled={blocked}
        aria-busy={busyFullResync || undefined}
        title="Re-reads every book in your Kindle Notebook, not just the ones that changed."
        onClick={() => onCommand("full_resync")}
      >
        {busyFullResync ? "Asking your browser…" : "Full resync"}
      </button>

      {status === "failed" ? (
        <Link className="btn btn--ghost" to="/app/import">
          Import a file instead
        </Link>
      ) : null}

      <span className="subtle">
        {running
          ? "Commands run when this sync finishes."
          : "Sync now catches new highlights; full resync re-reads every book."}
      </span>
    </div>
  );
}

/**
 * Sign-in is the one failure the user fixes outside Relego, so the banner points at the notebook on
 * their own Amazon region and keeps the explanation short.
 */
function AuthExpiredBanner({
  failure,
  region,
}: {
  failure: SyncFailure | null;
  region: SyncRegion | null;
}) {
  const notebookUrl = region?.host ? `https://${region.host}/notebook` : null;

  return (
    <div className="callout" data-tone="warn">
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        aria-hidden="true"
      >
        <path d="M12 3.5 2.8 19.5h18.4L12 3.5Z" />
        <path d="M12 10v4.2M12 17.1v.6" />
      </svg>
      <div>
        <p>
          <strong>Amazon asked for a sign-in</strong>
          {failure?.detail ? ` — ${failure.detail}` : " — the notebook could not be read."}
        </p>
        {failure && failure.recoverySteps.length > 0 ? (
          <ol className="steps">
            {failure.recoverySteps.map((step) => (
              <li key={step}>{step}</li>
            ))}
          </ol>
        ) : null}
        <p className="hint-line">
          {notebookUrl ? (
            <>
              <a href={notebookUrl} target="_blank" rel="noreferrer noopener">
                Open your Kindle Notebook
                <span className="sr-only"> (opens Amazon in a new tab)</span>
              </a>{" "}
              and sign in there.{" "}
            </>
          ) : null}
          Relego never sees your Amazon password.
        </p>
      </div>
    </div>
  );
}

function JobProgress({ job }: { job: SyncJob }) {
  const total = job.booksTotal ?? 0;
  const done = job.booksDone ?? 0;
  const determinate = total > 0;
  const ratio = determinate ? Math.min(1, done / total) : 0;

  return (
    <div className="panel-block">
      <div
        className="progress"
        role="progressbar"
        aria-label="Sync progress"
        aria-valuemin={determinate ? 0 : undefined}
        aria-valuemax={determinate ? total : undefined}
        aria-valuenow={determinate ? done : undefined}
      >
        <i style={{ transform: `scaleX(${ratio})` }} />
      </div>
      <p className="subtle hint-line">
        {PHASE_LABELS[job.phase ?? ""] ?? "Starting"} ·{" "}
        {determinate
          ? `${formatCount(done)} of ${formatCount(total)} books`
          : "counting your books"}{" "}
        · {plural(job.highlightsNew, "new highlight")} so far
      </p>
    </div>
  );
}

function Outcome({ job }: { job: SyncJob }) {
  return (
    <div className="panel-block">
      <p className="outcome">{outcomeText(job)}</p>
      <p className="subtle hint-line">
        {job.mode === "full_resync" ? "Full resync · " : null}
        {plural(job.highlightsSeen, "highlight")} read
        {job.highlightsDuplicate > 0
          ? ` · ${plural(job.highlightsDuplicate, "duplicate")} skipped`
          : null}
      </p>
      {job.truncatedBooks > 0 ? (
        <p className="subtle hint-line">
          {plural(job.truncatedBooks, "book")} capped by the publisher, so Relego imported only the
          highlights Amazon returns.
        </p>
      ) : null}
    </div>
  );
}

function outcomeText(job: SyncJob): string {
  return job.highlightsNew > 0
    ? `Imported ${plural(job.highlightsNew, "new highlight")}`
    : "Nothing new — Relego already had every highlight in this sync.";
}

/** The failure codes are stable, so the page owns the sentence the user actually reads. */
const FAILURE_TITLES: Record<string, string> = {
  auth_expired: "Amazon signed your browser out",
  provider_contract_changed: "Amazon changed the Kindle Notebook page",
  network: "Relego could not reach Amazon or your server",
  server_rejected: "Your server refused part of the sync",
  interrupted: "The sync was interrupted",
};

function FailureNote({ failure }: { failure: SyncFailure }) {
  return (
    <div className="callout" data-tone="bad">
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        aria-hidden="true"
      >
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7.5v5.5M12 16.2v.6" />
      </svg>
      <div>
        <p>
          <strong>{FAILURE_TITLES[failure.code] ?? "The last sync did not finish"}</strong>{" "}
          <Tag tone="bad">{failure.code}</Tag>
          {failure.detail ? ` — ${failure.detail}` : null}
        </p>
        {failure.recoverySteps.length > 0 ? (
          <ol className="steps">
            {failure.recoverySteps.map((step) => (
              <li key={step}>{step}</li>
            ))}
          </ol>
        ) : null}
      </div>
    </div>
  );
}

function PairingPanel({
  provider,
  code,
  busy,
  onRegenerate,
  onCheck,
  confirming,
  onAsk,
  onDismiss,
  onCancel,
  cancelling,
  onCopy,
}: {
  provider: SyncProviderSummary;
  code: string | null;
  busy: boolean;
  onRegenerate: () => void;
  onCheck: () => void;
  confirming: boolean;
  onAsk: () => void;
  onDismiss: () => void;
  onCancel: () => void;
  cancelling: boolean;
  onCopy: (value: string, label: string) => Promise<void>;
}) {
  // The extension has to reach this server, so the address shown is the one this page is
  // served from.
  const serverUrl = window.location.origin;

  return (
    <div className="panel">
      <header>
        <h2>{STATE_LABELS["awaiting_pairing"]}</h2>
        <p>
          Enter these two values in the Relego extension to connect {provider.displayName}. This page
          updates by itself as soon as a browser pairs.
        </p>
      </header>

      <div className="field">
        <span className="label">Server URL</span>
        <div className="secret">
          <code>{serverUrl}</code>
          <button
            className="btn btn--ghost btn--sm"
            type="button"
            onClick={() => void onCopy(serverUrl, "Server URL")}
          >
            Copy
          </button>
        </div>
      </div>

      <div className="field">
        <span className="label">One-time code</span>
        {code ? (
          <div className="secret">
            <code>{code}</code>
            <button
              className="btn btn--ghost btn--sm"
              type="button"
              onClick={() => void onCopy(code, "Code")}
            >
              Copy
            </button>
          </div>
        ) : (
          <p className="notice">
            The code is shown once and this page no longer holds it. Generate a new one to pair a
            browser — the old code stops working.
          </p>
        )}
      </div>

      <ol className="steps">
        <li>Install the Relego extension in Chrome or Firefox and open its popup.</li>
        <li>Paste the server URL and the one-time code, then save.</li>
        <li>Choose your Amazon region in the extension. Only validated regions are offered.</li>
        <li>
          Sign in to that Amazon site in the same browser if you are not already, then let the first
          sync run.
        </li>
      </ol>

      <div className="inline">
        <button className="btn" type="button" onClick={onRegenerate} aria-busy={busy || undefined}>
          {busy ? "Generating…" : "Generate a new code"}
        </button>
        <button className="btn btn--ghost" type="button" onClick={onCheck}>
          Check again
        </button>
        {confirming ? (
          <span className="confirm" role="group" aria-label="Cancel setup">
            <span className="confirm-q">Cancel setup? Nothing has been imported yet.</span>
            <button
              className="btn btn--danger btn--sm"
              type="button"
              onClick={onCancel}
              aria-busy={cancelling || undefined}
            >
              {cancelling ? "Cancelling…" : "Yes, cancel"}
            </button>
            <button className="btn btn--ghost btn--sm" type="button" onClick={onDismiss}>
              Keep waiting
            </button>
          </span>
        ) : (
          <button className="btn btn--ghost" type="button" onClick={onAsk}>
            Cancel setup
          </button>
        )}
      </div>
    </div>
  );
}

const COVERAGE_KINDS = ["Coverage", "Sideloaded", "ExportLimit", "Availability"];

function Disclosures({
  disclosure,
  prerequisites,
}: {
  disclosure: SyncDisclosure;
  prerequisites: string[];
}) {
  const byKind = (kinds: string[]) => disclosure.items.filter((item) => kinds.includes(item.kind));

  return (
    <>
      <section className="section" aria-labelledby="sync-coverage-h">
        <h2 className="section-title" id="sync-coverage-h">
          Coverage &amp; limits
        </h2>
        <dl className="tips">
          {byKind(COVERAGE_KINDS).map((item) => (
            <div className="tip" key={item.kind}>
              <dt>{item.title}</dt>
              <dd>{item.body}</dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="section" aria-labelledby="sync-needs-h">
        <h2 className="section-title" id="sync-needs-h">
          What this needs
        </h2>
        <ul className="plain-list">
          {prerequisites.map((item) => (
            <li key={item}>{item}</li>
          ))}
          {byKind(["Prerequisite"]).map((item) => (
            <li key={item.kind}>{item.body}</li>
          ))}
        </ul>
      </section>

      <section className="section" aria-labelledby="sync-privacy-h">
        <h2 className="section-title" id="sync-privacy-h">
          Privacy
        </h2>
        <dl className="tips">
          {byKind(["Privacy"]).map((item) => (
            <div className="tip" key={item.kind}>
              <dt>{item.title}</dt>
              <dd>{item.body}</dd>
            </div>
          ))}
        </dl>
      </section>
    </>
  );
}
