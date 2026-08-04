import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { InfoIcon } from "../components/icons";
import { ThemeSwitch } from "../components/ThemeSwitch";
import { ErrorNote, Tag } from "../components/ui";
import { api, ApiError } from "../lib/api";
import { capitalise, DAYS } from "../lib/format";
import { useToasts } from "../lib/toasts";
import type { SettingsResponse, SmtpSettingsResponse } from "../lib/types";

const SECTIONS = [
  { id: "s-delivery", label: "Where recaps go" },
  { id: "s-schedule", label: "When they go out" },
  { id: "s-smtp", label: "Email server" },
  { id: "s-conn", label: "Connection" },
  { id: "s-appearance", label: "Appearance" },
] as const;

function looksLikeEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
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
          <DeliveryPanel />
          <SchedulePanel />
          <SmtpPanel />
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
        </div>
      </div>
    </section>
  );
}

function useSettingsForm() {
  const queryClient = useQueryClient();
  const { push } = useToasts();

  const query = useQuery({ queryKey: ["settings"], queryFn: api.settings, retry: false });

  const save = useMutation({
    mutationFn: api.updateSettings,
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
  const { query, save, push } = useSettingsForm();
  const [kindleEmail, setKindleEmail] = useState("");
  const [deliveryEmail, setDeliveryEmail] = useState("");
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    if (!query.data) return;
    setKindleEmail(query.data.kindleEmail ?? "");
    setDeliveryEmail(query.data.deliveryEmail ?? "");
  }, [query.data]);

  const kindleError =
    touched && kindleEmail.trim() && !looksLikeEmail(kindleEmail)
      ? "That doesn't look like an email address. It needs a domain, like you@kindle.com."
      : null;

  const inboxError =
    touched && deliveryEmail.trim() && !looksLikeEmail(deliveryEmail)
      ? "That doesn't look like an email address. It needs a domain, like me@inbox.com."
      : null;

  const test = useMutation({
    mutationFn: (which: "kindle" | "inbox") =>
      which === "kindle" ? api.testKindleEmail() : api.testRecapEmail(),
    onSuccess: () => push("Test email sent."),
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not send the test email.", {
        tone: "bad",
      }),
  });

  return (
    <section className="panel" id="s-delivery">
      <header>
        <h2>Where recaps go</h2>
        <p>
          Set at least one destination. Your reader gets an EPUB via Send-to-Kindle; your inbox
          gets an HTML email.
        </p>
      </header>

      <form
        onSubmit={(event) => {
          event.preventDefault();
          setTouched(true);
          if (kindleError || inboxError) return;
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
              aria-invalid={kindleError ? true : undefined}
              aria-describedby={kindleError ? "kindle-err" : "kindle-help"}
              onChange={(event) => setKindleEmail(event.target.value)}
              onBlur={() => setTouched(true)}
            />
            <button
              className="btn"
              type="button"
              disabled={!looksLikeEmail(kindleEmail) || test.isPending}
              onClick={() => test.mutate("kindle")}
            >
              Send test
            </button>
          </div>
          {kindleError ? (
            <span className="err" id="kindle-err">
              {kindleError}
            </span>
          ) : (
            <span className="help" id="kindle-help">
              Add your Relego sender address to your Amazon approved-sender list first.
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
              aria-invalid={inboxError ? true : undefined}
              aria-describedby={inboxError ? "inbox-err" : "inbox-help"}
              onChange={(event) => setDeliveryEmail(event.target.value)}
              onBlur={() => setTouched(true)}
            />
            <button
              className="btn"
              type="button"
              disabled={!looksLikeEmail(deliveryEmail) || test.isPending}
              onClick={() => test.mutate("inbox")}
            >
              Send test
            </button>
          </div>
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
          <button className="btn btn--primary" type="submit" disabled={save.isPending}>
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
      <section className="panel" id="s-schedule">
        <header>
          <h2>When recaps go out</h2>
        </header>
        <div className="skel" style={{ width: "60%" }} />
      </section>
    );
  }

  const countValid = form.count >= 1 && form.count <= 15;

  return (
    <section className="panel" id="s-schedule">
      <header>
        <h2>When recaps go out</h2>
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
      push("Email server settings saved.");
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not save those settings.", {
        tone: "bad",
      }),
  });

  const test = useMutation({
    mutationFn: () => api.testSmtp(),
    onSuccess: (result) =>
      push(result.message, { tone: result.success ? "ok" : "bad" }),
    onError: (error) =>
      push(error instanceof Error ? error.message : "The connection test failed.", {
        tone: "bad",
      }),
  });

  const fieldError = (name: string) =>
    save.error instanceof ApiError ? save.error.field(name) : undefined;

  if (query.isError) {
    return (
      <section className="panel" id="s-smtp">
        <header>
          <h2>Email server</h2>
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
      <section className="panel" id="s-smtp">
        <header>
          <h2>Email server</h2>
        </header>
        <div className="skel" style={{ width: "60%" }} />
      </section>
    );
  }

  const fromEnvironment = query.data?.source === "environment";

  return (
    <section className="panel" id="s-smtp">
      <header>
        <h2>Email server</h2>
        <p>How Relego actually sends mail.</p>
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
          save.mutate();
        }}
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
          <button className="btn btn--primary" type="submit" disabled={save.isPending}>
            {save.isPending ? "Saving…" : "Save changes"}
          </button>
          <button
            className="btn"
            type="button"
            onClick={() => test.mutate()}
            disabled={test.isPending}
          >
            {test.isPending ? "Testing…" : "Test connection"}
          </button>
        </div>
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
