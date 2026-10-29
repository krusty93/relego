import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { CheckIcon, CrossIcon, InfoIcon } from "../components/icons";
import { ThemeSwitch } from "../components/ThemeSwitch";
import { ErrorNote, Tag } from "../components/ui";
import { api, ApiError } from "../lib/api";
import { capitalise, DAYS } from "../lib/format";
import { useToasts } from "../lib/toasts";
import type {
  SettingsResponse,
  SmtpSettingsResponse,
  UpdateSettingsRequest,
  UpdateSmtpSettingsRequest,
} from "../lib/types";

const SECTIONS = [
  { id: "s-conn", label: "Connection" },
  { id: "s-appearance", label: "Appearance" },
  { id: "s-recaps", label: "Recaps" },
] as const;

function looksLikeEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
}

/**
 * Send-to-Kindle only accepts mail at Amazon's own domain, so a typo here fails silently
 * hours later when the recap is dropped. `free.kindle.com` is Amazon's own wifi-delivery
 * variant, so subdomains are allowed.
 */
function isKindleAddress(value: string): boolean {
  const domain = value.trim().toLowerCase().split("@")[1];
  return domain === "kindle.com" || domain?.endsWith(".kindle.com") === true;
}

interface TestResult {
  success: boolean;
  message: string;
}

function useSettingsTest<T>(send: (values: T) => Promise<string>) {
  const { push } = useToasts();
  const revision = useRef(0);
  const [result, setResult] = useState<TestResult | null>(null);
  const test = useMutation({
    mutationFn: send,
    onMutate: () => {
      setResult(null);
      return revision.current;
    },
    onSuccess: (message, _values, testedRevision) => {
      if (testedRevision !== revision.current) return;
      setResult({ success: true, message });
      push(message);
    },
    onError: (error, _values, testedRevision) => {
      if (testedRevision !== revision.current) return;
      const message = error instanceof Error ? error.message : "The test failed. Try again.";
      setResult({ success: false, message });
      push(message, { tone: "bad" });
    },
  });

  function invalidate() {
    revision.current += 1;
    setResult(null);
  }

  return { test, result, invalidate };
}

function TestStatus({ result }: { result: TestResult | null }) {
  return (
    <div role="status" className="test-result" data-tone={result?.success ? "ok" : "bad"}>
      {result ? (
        <>
          {result.success ? <CheckIcon /> : <CrossIcon />}
          <span>{result.success ? "Test succeeded" : "Test failed"}: {result.message}</span>
        </>
      ) : null}
    </div>
  );
}

export function SettingsPage() {
  const [current, setCurrent] = useState<string>(SECTIONS[0].id);

  // Highlights the section nav entry for whichever panel is in view.
  useEffect(() => {
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
        if (visible) setCurrent(visible.target.id);
      },
      { rootMargin: "-80px 0px -60% 0px" },
    );

    for (const section of SECTIONS) {
      const element = document.getElementById(section.id);
      if (element) observer.observe(element);
    }

    return () => observer.disconnect();
  }, []);

  return (
    <section className="view" aria-labelledby="st-h">
      <div className="view-head">
        <div>
          <h1 id="st-h">Settings</h1>
          <p>Changes apply on save. Nothing is sent anywhere until you ask.</p>
        </div>
      </div>

      <div className="settings">
        <nav className="settings-nav" aria-label="Settings sections">
          {SECTIONS.map((section) => (
            <a
              key={section.id}
              href={`#${section.id}`}
              {...(current === section.id ? { "aria-current": "true" as const } : {})}
            >
              {section.label}
            </a>
          ))}
        </nav>

        <div>
          <ConnectionPanel />

          <section className="panel" id="s-appearance">
            <header>
              <h2>Appearance</h2>
              <p>Auto follows your operating system setting.</p>
            </header>
            <div className="field">
              <span className="label" id="theme-label">
                Theme
              </span>
              <ThemeSwitch wide labelledBy="theme-label" />
            </div>
          </section>

          <section className="panel" id="s-recaps" aria-labelledby="recaps-heading">
            <header>
              <h2 id="recaps-heading">Recaps</h2>
            </header>
            <SchedulePanel />
            <SmtpPanel />
            <DeliveryPanel />
          </section>
        </div>
      </div>
    </section>
  );
}

function useSettingsForm() {
  const queryClient = useQueryClient();
  const { push } = useToasts();

  const query = useQuery({ queryKey: ["settings"], queryFn: api.settings, retry: false });
  const current = query.data;

  const save = useMutation({
    // The server treats an absent deliveryEmail as "clear it", so every partial
    // save has to carry the address it isn't editing or the Schedule form would
    // silently wipe the delivery address.
    mutationFn: (body: UpdateSettingsRequest) =>
      api.updateSettings({ deliveryEmail: current?.deliveryEmail ?? "", ...body }),
    onSuccess: (data) => {
      queryClient.setQueryData(["settings"], data);
      void queryClient.invalidateQueries({ queryKey: ["status"] });
      push("Settings saved.");
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not save those settings.", {
        tone: "bad",
      }),
  });

  return { query, save, push };
}

function DeliveryPanel() {
  const { query, save } = useSettingsForm();
  const [kindleEmail, setKindleEmail] = useState("");
  const [deliveryEmail, setDeliveryEmail] = useState("");
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    if (!query.data) return;
    setKindleEmail(query.data.kindleEmail ?? "");
    setDeliveryEmail(query.data.deliveryEmail ?? "");
  }, [query.data?.kindleEmail, query.data?.deliveryEmail]);

  const kindleValid = looksLikeEmail(kindleEmail) && isKindleAddress(kindleEmail);

  const kindleError = !touched || !kindleEmail.trim()
    ? null
    : !looksLikeEmail(kindleEmail)
      ? "That doesn't look like an email address. It needs a domain, like you@kindle.com."
      : !isKindleAddress(kindleEmail)
        ? "Send-to-Kindle addresses end in @kindle.com. Check the Preferences page in your Amazon account for yours."
        : null;

  const inboxError =
    touched && deliveryEmail.trim() && !looksLikeEmail(deliveryEmail)
      ? "That doesn't look like an email address. It needs a domain, like me@inbox.com."
      : null;

  const kindleTest = useSettingsTest(async (address: string) => {
    await api.testKindleEmail(address);
    return "Test email sent.";
  });
  const inboxTest = useSettingsTest(async (address: string) => {
    await api.testRecapEmail(address);
    return "Test email sent.";
  });
  const kindleChanged = kindleEmail.trim() !== (query.data?.kindleEmail ?? "");
  const inboxChanged = deliveryEmail.trim() !== (query.data?.deliveryEmail ?? "");
  const canSave = query.isSuccess && (kindleChanged || inboxChanged) &&
    (!kindleEmail.trim() || kindleValid) &&
    (!deliveryEmail.trim() || looksLikeEmail(deliveryEmail)) &&
    (!kindleChanged || !kindleEmail.trim() || kindleTest.result?.success === true) &&
    (!inboxChanged || !deliveryEmail.trim() || inboxTest.result?.success === true) &&
    !kindleTest.test.isPending && !inboxTest.test.isPending && !save.isPending;

  return (
    <section className="settings-section" id="s-delivery">
      <header>
        <h3>Where recaps go</h3>
        <p>
          Set at least one destination. Your reader gets an EPUB via Send-to-Kindle; your inbox
          gets an HTML email.
        </p>
        <p>Test each changed address before saving. Tests use your saved email server settings and do not save changes.</p>
      </header>

      <form
        onSubmit={(event) => {
          event.preventDefault();
          setTouched(true);
          if (!canSave) return;
          save.mutate({ kindleEmail: kindleEmail.trim(), deliveryEmail: deliveryEmail.trim() });
        }}
      >
        <div className="field">
          <label htmlFor="kindle">Send-to-Kindle address</label>
          <div className="inline">
            <input
              className="control"
              id="kindle"
              type="email"
              value={kindleEmail}
              disabled={save.isPending}
              aria-invalid={kindleError ? true : undefined}
              aria-describedby={kindleError ? "kindle-err" : "kindle-help"}
              onChange={(event) => {
                setKindleEmail(event.target.value);
                kindleTest.invalidate();
              }}
              onBlur={() => setTouched(true)}
            />
            <button
              className="btn"
              type="button"
              disabled={!query.isSuccess || !kindleValid || kindleTest.test.isPending || save.isPending}
              onClick={() => kindleTest.test.mutate(kindleEmail.trim())}
            >
              {kindleTest.test.isPending ? "Sending…" : "Send test"}
            </button>
          </div>
          <TestStatus result={kindleTest.result} />
          {kindleError ? (
            <span className="err" id="kindle-err">
              {kindleError}
            </span>
          ) : (
            <span className="help" id="kindle-help">
              Ends in @kindle.com. Add your Relego sender address to your Amazon
              approved-sender list first.
            </span>
          )}
        </div>

        <div className="field">
          <label htmlFor="inbox">Inbox address</label>
          <div className="inline">
            <input
              className="control"
              id="inbox"
              type="email"
              value={deliveryEmail}
              disabled={save.isPending}
              aria-invalid={inboxError ? true : undefined}
              aria-describedby={inboxError ? "inbox-err" : "inbox-help"}
              onChange={(event) => {
                setDeliveryEmail(event.target.value);
                inboxTest.invalidate();
              }}
              onBlur={() => setTouched(true)}
            />
            <button
              className="btn"
              type="button"
              disabled={!query.isSuccess || !looksLikeEmail(deliveryEmail) || inboxTest.test.isPending || save.isPending}
              onClick={() => inboxTest.test.mutate(deliveryEmail.trim())}
            >
              {inboxTest.test.isPending ? "Sending…" : "Send test"}
            </button>
          </div>
          <TestStatus result={inboxTest.result} />
          {inboxError ? (
            <span className="err" id="inbox-err">
              {inboxError}
            </span>
          ) : (
            <span className="help" id="inbox-help">
              Optional. Leave empty to send only to your reader.
            </span>
          )}
        </div>

        <div className="inline dialog-actions">
          <button className="btn btn--primary" type="submit" disabled={!canSave}>
            {save.isPending ? "Saving…" : "Save changes"}
          </button>
        </div>
      </form>
    </section>
  );
}

function SchedulePanel() {
  const { query, save } = useSettingsForm();
  const [form, setForm] = useState<Pick<
    SettingsResponse,
    "schedule" | "deliveryDay" | "deliveryTime" | "count"
  > | null>(null);

  useEffect(() => {
    if (!query.data) return;
    setForm({
      schedule: query.data.schedule,
      deliveryDay: query.data.deliveryDay,
      deliveryTime: query.data.deliveryTime,
      count: query.data.count,
    });
  }, [query.data]);

  if (!form) {
    return (
      <section className="settings-section" id="s-schedule">
        <header>
          <h3>When recaps go out</h3>
        </header>
        <div className="skel" style={{ width: "60%" }} />
      </section>
    );
  }

  const countValid = form.count >= 1 && form.count <= 15;

  return (
    <section className="settings-section" id="s-schedule">
      <header>
        <h3>When recaps go out</h3>
      </header>

      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (!countValid) return;
          save.mutate({
            schedule: form.schedule,
            deliveryDay: form.schedule === "weekly" ? (form.deliveryDay ?? "sunday") : null,
            deliveryTime: form.deliveryTime,
            count: form.count,
          });
        }}
      >
        <div className="field">
          <span className="label" id="cadence-label">
            Cadence
          </span>
          <div className="seg seg--start" role="group" aria-labelledby="cadence-label">
            {(["daily", "weekly"] as const).map((option) => (
              <button
                key={option}
                type="button"
                aria-pressed={form.schedule === option}
                onClick={() => setForm({ ...form, schedule: option })}
              >
                {capitalise(option)}
              </button>
            ))}
          </div>
        </div>

        {form.schedule === "weekly" ? (
          <div className="field">
            <label htmlFor="day">Day</label>
            <select
              className="control"
              id="day"
              value={form.deliveryDay ?? "sunday"}
              onChange={(event) => setForm({ ...form, deliveryDay: event.target.value })}
            >
              {DAYS.map((day) => (
                <option key={day} value={day}>
                  {capitalise(day)}
                </option>
              ))}
            </select>
          </div>
        ) : null}

        <div className="row">
          <div className="field">
            <label htmlFor="time">Time</label>
            <input
              className="control"
              id="time"
              type="time"
              value={form.deliveryTime}
              onChange={(event) => setForm({ ...form, deliveryTime: event.target.value })}
            />
            <span className="help">Server timezone: {query.data?.timezone ?? "—"}</span>
          </div>

          <div className="field">
            <label htmlFor="count">Highlights per recap</label>
            <input
              className="control"
              id="count"
              type="number"
              min={1}
              max={15}
              value={form.count}
              aria-invalid={countValid ? undefined : true}
              aria-describedby="count-help"
              onChange={(event) => setForm({ ...form, count: Number(event.target.value) })}
            />
            <span className={countValid ? "help" : "err"} id="count-help">
              Between 1 and 15.
            </span>
          </div>
        </div>

        <div className="inline dialog-actions">
          <button
            className="btn btn--primary"
            type="submit"
            disabled={save.isPending || !countValid}
          >
            {save.isPending ? "Saving…" : "Save changes"}
          </button>
        </div>
      </form>
    </section>
  );
}

function SmtpPanel() {
  const queryClient = useQueryClient();
  const { push } = useToasts();

  const query = useQuery({ queryKey: ["smtp"], queryFn: api.smtp, retry: false });

  const [form, setForm] = useState<Pick<
    SmtpSettingsResponse,
    "host" | "port" | "fromAddress" | "username"
  > | null>(null);
  const [password, setPassword] = useState("");
  const verification = useSettingsTest(async (settings: UpdateSmtpSettingsRequest) => {
    const result = await api.testSmtp(settings);
    if (!result.success) throw new Error(result.message);
    return result.message;
  });

  useEffect(() => {
    if (!query.data) return;
    setForm({
      host: query.data.host,
      port: query.data.port,
      fromAddress: query.data.fromAddress,
      username: query.data.username,
    });
    setPassword("");
  }, [query.data]);

  const save = useMutation({
    mutationFn: () =>
      api.updateSmtp({
        ...form!,
        // An untouched password field must not clear the stored secret.
        ...(password ? { password } : {}),
      }),
    onSuccess: (data) => {
      queryClient.setQueryData(["smtp"], data);
      setPassword("");
      verification.invalidate();
      push("Email server settings saved.");
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not save those settings.", {
        tone: "bad",
      }),
  });

  const fieldError = (name: string) =>
    verification.result?.success === false && verification.test.error instanceof ApiError
      ? verification.test.error.field(name)
      : save.error instanceof ApiError ? save.error.field(name) : undefined;

  if (query.isError) {
    return (
      <section className="settings-section" id="s-smtp">
        <header>
          <h3>Email server</h3>
        </header>
        <ErrorNote
          message={query.error instanceof Error ? query.error.message : "Could not load settings."}
          onRetry={() => void query.refetch()}
        />
      </section>
    );
  }

  if (!form) {
    return (
      <section className="settings-section" id="s-smtp">
        <header>
          <h3>Email server</h3>
        </header>
        <div className="skel" style={{ width: "60%" }} />
      </section>
    );
  }

  const fromEnvironment = query.data?.source === "environment";
  const valid = !!form.host.trim() && Number.isInteger(form.port) &&
    form.port >= 1 && form.port <= 65535 && looksLikeEmail(form.fromAddress);
  const changed = form.host !== query.data?.host || form.port !== query.data?.port ||
    form.fromAddress !== query.data?.fromAddress || form.username !== query.data?.username ||
    password !== "";
  const canSave = valid && (changed || fromEnvironment) && verification.result?.success === true &&
    !verification.test.isPending && !save.isPending;

  return (
    <section className="settings-section" id="s-smtp">
      <header>
        <h3>Email server</h3>
        <p>Test your changes before saving. The connection test sends a message without saving these settings.</p>
      </header>

      {fromEnvironment ? (
        <div className="callout">
          <InfoIcon strokeWidth={1.8} />
          <span>
            <strong>Seeded from environment.</strong> These values came from your compose file.
            Saving here stores them in the database, which takes over from the environment.
          </span>
        </div>
      ) : null}

      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (canSave) save.mutate();
        }}
        onChange={() => verification.invalidate()}
      >
        <div className="row">
          <div className="field">
            <label htmlFor="host">
              Host{" "}
              {fromEnvironment ? <span className="env-badge">from env</span> : null}
            </label>
            <input
              className="control"
              id="host"
              value={form.host}
              disabled={save.isPending}
              aria-invalid={fieldError("host") ? true : undefined}
              {...(fieldError("host") ? { "aria-describedby": "host-err" } : {})}
              onChange={(event) => setForm({ ...form, host: event.target.value })}
            />
            {fieldError("host") ? (
              <span className="err" id="host-err">
                {fieldError("host")}
              </span>
            ) : null}
          </div>

          <div className="field">
            <label htmlFor="port">Port</label>
            <input
              className="control"
              id="port"
              type="number"
              min={1}
              max={65535}
              value={form.port}
              disabled={save.isPending}
              onChange={(event) => setForm({ ...form, port: Number(event.target.value) })}
            />
          </div>
        </div>

        <div className="row">
          <div className="field">
            <label htmlFor="user">Username</label>
            <input
              className="control"
              id="user"
              autoComplete="username"
              value={form.username}
              disabled={save.isPending}
              onChange={(event) => setForm({ ...form, username: event.target.value })}
            />
          </div>

          <div className="field">
            <label htmlFor="pass">Password</label>
            <input
              className="control"
              id="pass"
              type="password"
              autoComplete="current-password"
              placeholder={query.data?.passwordSet ? "Saved — leave blank to keep" : "Not set"}
              value={password}
              disabled={save.isPending}
              aria-describedby="pass-help"
              onChange={(event) => setPassword(event.target.value)}
            />
            <span className="help" id="pass-help">
              Write-only. The server never sends it back to the browser.
            </span>
          </div>
        </div>

        <div className="field">
          <label htmlFor="from">From address</label>
          <input
            className="control"
            id="from"
            type="email"
            value={form.fromAddress}
            disabled={save.isPending}
            aria-invalid={fieldError("fromAddress") ? true : undefined}
            {...(fieldError("fromAddress") ? { "aria-describedby": "from-err" } : {})}
            onChange={(event) => setForm({ ...form, fromAddress: event.target.value })}
          />
          {fieldError("fromAddress") ? (
            <span className="err" id="from-err">
              {fieldError("fromAddress")}
            </span>
          ) : null}
        </div>

        <div className="inline dialog-actions">
          <button className="btn btn--primary" type="submit" disabled={!canSave}>
            {save.isPending ? "Saving…" : "Save changes"}
          </button>
          <button
            className="btn"
            type="button"
            onClick={() => verification.test.mutate({
              ...form,
              ...(password ? { password } : {}),
            })}
            disabled={!valid || verification.test.isPending || save.isPending}
          >
            {verification.test.isPending ? "Testing…" : "Test connection"}
          </button>
        </div>
        <TestStatus result={verification.result} />
      </form>
    </section>
  );
}

function ConnectionPanel() {
  const status = useQuery({ queryKey: ["status"], queryFn: api.status, retry: false });

  return (
    <section className="panel" id="s-conn">
      <header>
        <h2>Connection</h2>
        <p>
          Relego has no login. Anyone who can reach this page can change these settings, so keep
          it on a network you trust.
        </p>
      </header>

      <dl className="dl">
        <dt>Server URL</dt>
        <dd>
          <code>{window.location.origin}</code>
        </dd>
        <dt>Server version</dt>
        <dd>{status.data?.serverVersion ?? "—"}</dd>
        <dt>Status</dt>
        <dd>
          {status.isSuccess ? (
            <Tag tone="ok">Connected</Tag>
          ) : status.isError ? (
            <Tag tone="bad">Disconnected</Tag>
          ) : (
            <span className="subtle">Checking…</span>
          )}
        </dd>
        <dt>Books</dt>
        <dd>{status.data ? status.data.totalBooks : "—"}</dd>
        <dt>Highlights</dt>
        <dd>{status.data ? status.data.totalHighlights : "—"}</dd>
      </dl>
    </section>
  );
}
