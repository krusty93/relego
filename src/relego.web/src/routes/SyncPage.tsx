import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Link } from "react-router";
import { AsyncSection, EmptyState, ErrorNote, SkeletonRows, Tag } from "../components/ui";
import { syncApi } from "../lib/api";
import {
  describeFrequency,
  formatCount,
  formatDate,
  formatDateTime,
  formatDuration,
  plural,
  secondsSince,
} from "../lib/format";
import { useToasts } from "../lib/toasts";
import type {
  SyncDisclosure,
  SyncFailure,
  SyncJob,
  SyncProviderSummary,
  SyncRegion,
  SyncReminder,
  SyncStatusResponse,
} from "../lib/types";

/**
 * While something is moving the page follows it closely: a running sync, a browser that is pairing,
 * and a command waiting for the browser's next check-in all change within seconds. Only an idle
 * connection polls slowly. Polling continues in a background window, because the extension popup
 * that pairs or the browser running the sync is usually in front of this page.
 */
const POLL_ACTIVE_MS = 2_000;
const POLL_IDLE_MS = 30_000;

/**
 * How long a check-in gap stops being normal. The extension reports every sixty seconds, so two
 * minutes of silence means the browser is asleep or closed and the request will wait.
 */
const STALE_CHECK_IN_SECONDS = 120;

const PREFERRED_PROVIDER_ID = "kindle-cloud";

/** A command the browser has still to pick up, or `null` when it has none waiting. */
function queuedCommand(value: string): "sync" | "full_resync" | null {
  return value === "sync" || value === "full_resync" ? value : null;
}

/**
 * Whether the status is changing quickly enough to deserve the active cadence: a running sync, a
 * browser that is pairing, or a command the browser has still to pick up.
 */
function isActive(data: SyncStatusResponse | undefined): boolean {
  if (data === undefined) return false;

  if (data.status === "syncing" || data.status === "awaiting_pairing" || data.currentJob !== null) {
    return true;
  }

  // A queued command is only worth watching while the browser is still checking in; once it stops,
  // the request can wait for hours and there is nothing new to see.
  return queuedCommand(data.schedule.pendingCommand) !== null && data.connection.browserReporting;
}

/** Re-renders on an interval, so elapsed and relative times stay true while something runs. */
function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs);

    return () => window.clearInterval(timer);
  }, [intervalMs]);

  return now;
}

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
      <div className="view-head sync-head">
        <div>
          <h1 id="sync-h">Kindle Notebook Sync</h1>
          <p>Keep your Kindle Notebook highlights flowing into Relego automatically.</p>
          <p>
            <Link to="/app/import">Import a highlights file</Link> instead if you would rather
            keep it manual.
          </p>
        </div>
      </div>

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
    refetchInterval: (query) => (isActive(query.state.data) ? POLL_ACTIVE_MS : POLL_IDLE_MS),
    refetchIntervalInBackground: true,
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

  // Dismissing hides the banner for this quiet period only; new content starts a new one.
  const dismissReminder = useMutation({
    mutationFn: (reminderId: number) =>
      syncApi.dismissReminder(provider.providerId, reminderId),
    onSuccess: () => {
      refresh();
      push("Reminder dismissed. It comes back only if nothing new arrives again.");
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not dismiss the reminder.", {
        tone: "bad",
      }),
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

  // The frequency is the extension's to apply, so a stored value is written into the status cache
  // straight away: the page can then say whether the browser has picked it up yet.
  const schedule = useMutation({
    mutationFn: (intervalMinutes: number) =>
      syncApi.schedule(provider.providerId, intervalMinutes),
    onSuccess: (response) => {
      queryClient.setQueryData<SyncStatusResponse>(["sync", provider.providerId], (current) =>
        current
          ? {
              ...current,
              schedule: {
                ...current.schedule,
                intervalMinutes: response.intervalMinutes,
                applied: response.applied,
                nextRunAt: response.nextRunAt,
              },
            }
          : current,
      );
      refresh();
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not change the sync frequency.", {
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

      <section className="sync-needs" aria-labelledby="sync-needs-h">
        <h2 className="section-title" id="sync-needs-h">
          What this needs
        </h2>
        <p>
          Keep Relego and your browser running, with the extension allowed to access your Kindle
          Notebook. Sign in to the Amazon account registered on your Kindle device or app.
        </p>
        <p>
          Get the extension from the{" "}
          <a href="https://chromewebstore.google.com/detail/relego" target="_blank" rel="noreferrer">
            Chrome Web Store
          </a>
          ,{" "}
          <a
            href="https://addons.mozilla.org/en-US/firefox/search/?q=relego"
            target="_blank"
            rel="noreferrer"
          >
            Firefox Add-ons
          </a>
          , or{" "}
          <a href="https://github.com/krusty93/relego/releases" target="_blank" rel="noreferrer">
            GitHub releases
          </a>
          .
        </p>
      </section>

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

      {isFirstRun ? null : isPairing ? (
        <PairingPanel
          provider={provider}
          code={code}
          busy={connect.isPending}
          onRegenerate={() => connect.mutate()}
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
          onCopy={copy}
          confirming={confirming === "disconnect"}
          onAsk={() => setConfirming("disconnect")}
          onDismiss={() => setConfirming(null)}
          onDisconnect={() => disconnect.mutate()}
          disconnecting={disconnect.isPending}
          onCommand={(value) => command.mutate(value)}
          asking={command.isPending ? (command.variables ?? null) : null}
          onDismissReminder={(reminderId) => dismissReminder.mutate(reminderId)}
          dismissingReminder={dismissReminder.isPending}
          onSchedule={async (intervalMinutes) => {
            try {
              await schedule.mutateAsync(intervalMinutes);
            } catch {
              // Reported by the mutation's onError; the stored frequency is unchanged.
            }
          }}
          scheduling={schedule.isPending}
        />
      )}

      <Disclosures disclosure={data.disclosure} />
    </>
  );
}

function StatusPanel({
  data,
  onCopy,
  confirming,
  onAsk,
  onDismiss,
  onDisconnect,
  disconnecting,
  onCommand,
  asking,
  onSchedule,
  scheduling,
  onDismissReminder,
  dismissingReminder,
}: {
  data: SyncStatusResponse;
  onCopy: (value: string, label: string) => Promise<void>;
  confirming: boolean;
  onAsk: () => void;
  onDismiss: () => void;
  onDisconnect: () => void;
  disconnecting: boolean;
  onCommand: (command: "sync" | "full_resync") => void;
  /** Command whose request is still in flight; the label reads "Asking your browser…". */
  asking: "sync" | "full_resync" | null;
  onSchedule: (intervalMinutes: number) => Promise<void>;
  scheduling: boolean;
  onDismissReminder: (reminderId: number) => void;
  dismissingReminder: boolean;
}) {
  const { status, connection, currentJob, lastCompletedJob, failure, schedule, reminder } = data;
  const running = status === "syncing" && currentJob !== null;
  const needsSignIn = status === "auth_expired";
  const now = useNow(1_000);

  // A command lives on the server until the browser's next check-in, so it survives a reload; the
  // mutation's own value covers the moment between the click and the refetch that shows it.
  const queued = asking ?? queuedCommand(schedule.pendingCommand);
  const heartbeatAge = secondsSince(connection.lastHeartbeatAt, now);
  const stale = heartbeatAge !== null && heartbeatAge >= STALE_CHECK_IN_SECONDS;

  // The choice is an overlay on the stored frequency: it covers the round trip to the server and
  // clears once that settles, so an accepted value shows as the stored one and a rejected value
  // falls back to the frequency the server kept.
  const [chosen, setChosen] = useState<number | null>(null);
  const frequency = chosen ?? schedule.intervalMinutes;

  async function choose(intervalMinutes: number) {
    setChosen(intervalMinutes);
    await onSchedule(intervalMinutes);
    setChosen(null);
  }

  const nextRun = schedule.applied
    ? schedule.nextRunAt
      ? `Next sync ${formatDateTime(schedule.nextRunAt)}, as reported by your browser.`
      : "Your browser has not reported when the next sync is due."
    : "Applies when your browser next reports.";

  return (
    <div className="panel">
      <header>
        <h2>{STATE_LABELS[status] ?? "Sync"}</h2>
        <p>{STATE_SUMMARIES[status] ?? "Sync runs from the Relego browser extension."}</p>
      </header>

      {needsSignIn ? (
        <AuthExpiredBanner failure={failure} region={connection.region} />
      ) : status === "failed" && failure ? (
        <FailureNote failure={failure} onCopy={onCopy} />
      ) : reminder ? (
        <ReminderBanner
          reminder={reminder}
          dismissing={dismissingReminder}
          onDismiss={() => onDismissReminder(reminder.id)}
        />
      ) : null}

      <SyncActions
        status={status}
        running={running}
        asking={asking}
        queued={queued}
        onCommand={onCommand}
      />

      {queued !== null && !running ? (
        <RequestedNotice
          command={queued}
          heartbeatAge={heartbeatAge}
          stale={stale}
        />
      ) : null}

      {running && currentJob ? <JobProgress job={currentJob} /> : null}
      {!running && lastCompletedJob ? <Outcome job={lastCompletedJob} /> : null}

      <dl className="dl">
        <dt>Region</dt>
        <dd data-testid="sync-region">
          {connection.region ? (
            <>
              {connection.region.displayName}{" "}
              <span className="subtle">({connection.region.host})</span>
            </>
          ) : (
            <span className="subtle">Chosen in the extension</span>
          )}
        </dd>

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
        <dd data-testid="sync-heartbeat">
          {connection.lastHeartbeatAt ? (
            <>
              <time dateTime={connection.lastHeartbeatAt}>
                {formatDateTime(connection.lastHeartbeatAt)}
              </time>
              <span className="subtle">
                {" "}
                ({heartbeatAge === null ? "unknown" : `${formatDuration(heartbeatAge)} ago`})
              </span>
            </>
          ) : (
            <span className="subtle">Not yet</span>
          )}
        </dd>
      </dl>

      <p className="subtle hint-line">
        Your region is set in the Relego extension, which offers only the validated Amazon regions.
        Change it there and the next sync reads the new store.
      </p>

      <div className="field">
        <label htmlFor="sync-frequency">Sync frequency</label>
        <select
          className="control"
          id="sync-frequency"
          data-testid="sync-frequency"
          value={frequency}
          disabled={scheduling}
          aria-busy={scheduling || undefined}
          onChange={(event) => void choose(Number(event.target.value))}
        >
          {schedule.allowedIntervalMinutes.map((minutes) => (
            <option key={minutes} value={minutes}>
              {describeFrequency(minutes)}
            </option>
          ))}
        </select>
        <span className="help" data-testid="sync-next-run">
          {nextRun} Relego's server does not schedule it; your browser holds the timer and runs the
          sync.
        </span>
      </div>

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
  asking,
  queued,
  onCommand,
}: {
  status: string;
  running: boolean;
  asking: "sync" | "full_resync" | null;
  queued: "sync" | "full_resync" | null;
  onCommand: (command: "sync" | "full_resync") => void;
}) {
  // Only the round trip to the server is "asking"; once the command is queued the buttons stay
  // disabled under their own names, because the wait now belongs to the browser.
  const busySync = asking === "sync";
  const busyFullResync = asking === "full_resync";
  const blocked = running || queued !== null;

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
          : queued !== null
            ? "Waiting for your browser to check in; the buttons come back when it does."
            : "Sync now catches new highlights; full resync re-reads every book."}
      </span>
    </div>
  );
}

/**
 * What a queued command is doing while nothing runs yet.
 *
 * The extension picks commands up on its fixed check-in, so the honest answer is "waiting" plus the
 * evidence that the browser is still alive: the age of its last check-in, or a warning when that age
 * means it has gone away.
 */
function RequestedNotice({
  command,
  heartbeatAge,
  stale,
}: {
  command: "sync" | "full_resync";
  heartbeatAge: number | null;
  stale: boolean;
}) {
  const title = command === "full_resync" ? "Full resync requested" : "Sync requested";

  return (
    <div className="panel-block" data-testid="sync-requested">
      <p className="outcome">{title}</p>
      {stale ? (
        <p className="subtle hint-line">
          Your browser has not checked in for {formatDuration(heartbeatAge ?? 0)}, so it is not
          listening. Open the browser where the extension is installed; the request stays queued
          until it does.
        </p>
      ) : (
        <p className="subtle hint-line">
          Your browser picks this up on its next check-in, at most a minute away.
          {heartbeatAge === null
            ? null
            : ` Last check-in ${formatDuration(heartbeatAge)} ago.`}
        </p>
      )}
    </div>
  );
}

/**
 * The inactivity nudge. It is the only state the user clears from this page, so it carries its own
 * control: a reminder that keeps coming back after being answered is worse than no reminder.
 */
function ReminderBanner({
  reminder,
  dismissing,
  onDismiss,
}: {
  reminder: SyncReminder;
  dismissing: boolean;
  onDismiss: () => void;
}) {
  return (
    <div className="callout" data-testid="sync-reminder">
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        aria-hidden="true"
      >
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7.4V12l3.2 2" />
      </svg>
      <div>
        <p>
          <strong>Nothing new in {reminder.thresholdDays} days</strong>
        </p>
        <p className="hint-line">
          Relego has read no new highlights since{" "}
          <time dateTime={reminder.quietSince}>{formatDate(reminder.quietSince)}</time>. Your browser
          only reads the notebook while it is open — <strong>Sync now</strong> asks it to check, or{" "}
          <Link to="/app/import">import a highlights file</Link> to do it by hand.
        </p>
        <button
          className="btn btn--ghost btn--sm"
          type="button"
          disabled={dismissing}
          aria-busy={dismissing || undefined}
          onClick={onDismiss}
        >
          {dismissing ? "Dismissing…" : "Dismiss"}
        </button>
      </div>
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
  const now = useNow(1_000);
  const total = job.booksTotal ?? 0;
  const done = job.booksDone ?? 0;
  const determinate = total > 0;
  const ratio = determinate ? Math.min(1, done / total) : 0;

  // The job reports startedAt and a book counter, which is all a remaining time needs: books take
  // roughly the same time to read, so the rate so far extrapolates. It is withheld until one book is
  // done, because until then there is no rate to extrapolate.
  const elapsed = secondsSince(job.startedAt, now);
  const remaining =
    elapsed === null || !determinate || done === 0 || done >= total
      ? null
      : Math.round(((total - done) * elapsed) / done);

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
      <p className="subtle hint-line" data-testid="sync-timing">
        {elapsed === null ? (
          "Reading now."
        ) : (
          <>
            Running for {formatDuration(elapsed)}
            {remaining === null ? "." : ` · about ${formatDuration(remaining)} left.`}
          </>
        )}
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

function FailureNote({
  failure,
  onCopy,
}: {
  failure: SyncFailure;
  onCopy: (value: string, label: string) => Promise<void>;
}) {
  const diagnostics = failure.diagnostics;
  const counts = diagnostics
    ? Object.entries(diagnostics.counts)
        .map(([key, value]) => `${key} ${value}`)
        .join(" · ")
    : "";

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
        {diagnostics ? (
          <div className="diagnostics" data-testid="sync-failure-diagnostics">
            <span className="label">Diagnostics</span>
            <div className="secret">
              <code>{JSON.stringify(diagnostics)}</code>
              <button
                className="btn btn--ghost btn--sm"
                type="button"
                onClick={() => void onCopy(JSON.stringify(diagnostics), "Diagnostics")}
              >
                Copy
              </button>
            </div>
            <p className="subtle">
              Profile {diagnostics.profileVersion ?? "unknown"} stopped at{" "}
              <code>{diagnostics.check ?? "an unnamed check"}</code>
              {counts ? ` with ${counts}` : null}. Copy this into a bug report; it holds no page
              text.
            </p>
          </div>
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
          updates by itself within a couple of seconds of the browser pairing.
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

      <div className="field">
        <span className="label">Region</span>
        <p className="subtle" data-testid="pairing-region">
          Chosen in the extension. The popup lists the validated Amazon regions, so you pick one
          from a list rather than typing a host.
        </p>
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

const HOW_IT_WORKS_KINDS = ["Coverage", "Sideloaded", "ExportLimit", "Availability", "Privacy"];

function Disclosures({ disclosure }: { disclosure: SyncDisclosure }) {
  return (
    <section className="section" aria-labelledby="sync-coverage-h">
      <h2 className="section-title" id="sync-coverage-h">
        How it works
      </h2>
      <dl className="tips sync-details">
        {disclosure.items
          .filter((item) => HOW_IT_WORKS_KINDS.includes(item.kind))
          .map((item) => (
            <div className="tip" key={item.kind}>
              <dt>{item.title}</dt>
              <dd>{item.body}</dd>
            </div>
          ))}
      </dl>
    </section>
  );
}
