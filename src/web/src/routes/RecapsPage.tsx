import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { AsyncSection, EmptyState, Tag } from "../components/ui";
import { api } from "../lib/api";
import { describeSchedule, formatDateTime, formatDateTimeInZone, plural } from "../lib/format";
import { useToasts } from "../lib/toasts";

function resultTone(status: string): "ok" | "bad" | "excluded" {
  const normalised = status.toLowerCase();
  if (normalised === "sent" || normalised === "delivered") return "ok";
  if (normalised === "failed" || normalised === "error") return "bad";
  return "excluded";
}

export function RecapsPage() {
  const queryClient = useQueryClient();
  const { push } = useToasts();

  const status = useQuery({ queryKey: ["status"], queryFn: api.status, retry: false });
  const settings = useQuery({ queryKey: ["settings"], queryFn: api.settings, retry: false });
  const history = useQuery({ queryKey: ["recaps"], queryFn: api.recapHistory, retry: false });

  const sendNow = useMutation({
    mutationFn: api.sendRecapNow,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["recaps"] });
      void queryClient.invalidateQueries({ queryKey: ["status"] });
      push("Recap queued. It will be delivered shortly.");
    },
    onError: (error) =>
      push(error instanceof Error ? error.message : "Could not send the recap.", { tone: "bad" }),
  });

  const items = history.data?.items ?? [];

  const hasDestination = Boolean(
    settings.data?.kindleEmail?.trim() || settings.data?.deliveryEmail?.trim(),
  );
  const blockedReason = settings.isSuccess && !hasDestination ? "Add a reader address first." : null;

  const nextRecap = formatDateTimeInZone(status.data?.nextRecap, settings.data?.timezone);

  return (
    <section className="view" aria-labelledby="rc-h">
      <div className="view-head">
        <div>
          <h1 id="rc-h">Recaps</h1>
          <p>
            Relego picks highlights by weight and by how long since you last saw them, then emails
            them to your reader.
          </p>
        </div>
        <div className="actions">
          <button
            className="btn btn--primary"
            type="button"
            onClick={() => sendNow.mutate()}
            disabled={sendNow.isPending || blockedReason !== null}
            aria-describedby={blockedReason ? "rc-blocked" : undefined}
          >
            {sendNow.isPending ? "Sending…" : "Send recap now"}
          </button>
        </div>
      </div>

      {blockedReason ? (
        <p className="notice" id="rc-blocked">
          {blockedReason} <Link to="/settings">Open settings</Link> to choose where recaps are
          delivered.
        </p>
      ) : null}

      <div className="panel">
        <dl className="dl">
          <dt>Next recap</dt>
          <dd>
            {status.data?.nextRecap ? (
              <>
                <time dateTime={status.data.nextRecap}>{nextRecap.text}</time>{" "}
                <span className="subtle">{nextRecap.zone}</span>
              </>
            ) : (
              <span className="subtle">Not scheduled yet</span>
            )}
          </dd>

          <dt>Cadence</dt>
          <dd>
            {settings.data ? (
              <>
                {describeSchedule(
                  settings.data.schedule,
                  settings.data.deliveryDay,
                  settings.data.deliveryTime,
                )}{" "}
                · {plural(settings.data.count, "highlight")}
              </>
            ) : (
              <span className="subtle">—</span>
            )}
          </dd>

          <dt>Delivering to</dt>
          <dd>
            {settings.data?.kindleEmail || <span className="subtle">No reader address set</span>}
            {settings.data?.deliveryEmail ? (
              <>
                {" "}
                <span className="subtle">and</span> {settings.data.deliveryEmail}
              </>
            ) : null}
          </dd>

          <dt>Last recap</dt>
          <dd>
            {status.data?.lastRecapStatus ? (
              <>
                <Tag tone={resultTone(status.data.lastRecapStatus)}>
                  {status.data.lastRecapStatus}
                </Tag>
                {status.data.lastRecapError ? (
                  <span className="subtle"> · {status.data.lastRecapError}</span>
                ) : null}
              </>
            ) : (
              <span className="subtle">None sent yet</span>
            )}
          </dd>
        </dl>
      </div>

      <section className="section" aria-labelledby="rc-hist">
        <h2 id="rc-hist" className="section-title">
          Recent deliveries
        </h2>

        <AsyncSection
          isLoading={history.isPending}
          error={history.error}
          onRetry={() => void history.refetch()}
          isEmpty={items.length === 0}
          empty={
            <EmptyState title="No recaps sent yet">
              Once a recap goes out, every attempt shows up here with its result.
            </EmptyState>
          }
        >
          <div className="table-wrap">
            <table>
              <caption className="sr-only">Recap delivery history</caption>
              <thead>
                <tr>
                  <th scope="col">Scheduled</th>
                  <th scope="col">Delivered</th>
                  <th scope="col" className="num">
                    Attempts
                  </th>
                  <th scope="col">Result</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id} tabIndex={0}>
                    <td>{formatDateTime(item.scheduledFor)}</td>
                    <td className="muted">{formatDateTime(item.deliveredAt)}</td>
                    <td className="num">{item.attemptCount}</td>
                    <td>
                      <Tag tone={resultTone(item.status)}>
                        {item.errorMessage ?? item.status}
                      </Tag>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </AsyncSection>
      </section>
    </section>
  );
}
