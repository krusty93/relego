import { useQuery } from "@tanstack/react-query";
import { AsyncSection, EmptyState } from "../components/ui";
import { syncApi } from "../lib/api";

/**
 * Cloud sync surface. This is the route and data seam only; the connect wizard, live progress,
 * recovery and frequency controls land with the user-story tasks.
 */
export function SyncPage() {
  const providers = useQuery({ queryKey: ["sync", "providers"], queryFn: syncApi.providers });
  const provider =
    providers.data?.find((item) => item.providerId === "kindle-cloud") ?? providers.data?.[0];

  return (
    <section className="view" aria-labelledby="sync-h">
      <div className="view-head">
        <div>
          <h1 id="sync-h">Sync</h1>
          <p>Keep your Kindle Notebook highlights flowing into Relego automatically.</p>
        </div>
      </div>

      <AsyncSection
        isLoading={providers.isLoading}
        error={providers.error}
        onRetry={() => void providers.refetch()}
      >
        <EmptyState
          title="Connect Kindle cloud"
          action={
            <button className="btn" type="button" disabled>
              Connect Kindle cloud
            </button>
          }
        >
          {provider
            ? `Automatic sync runs from the ${provider.displayName} browser extension. The connect wizard arrives with the next task.`
            : "Automatic sync runs from a browser extension. The connect wizard arrives with the next task."}
        </EmptyState>
      </AsyncSection>
    </section>
  );
}
