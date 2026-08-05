import type { ReactNode } from "react";

export function Tag({ tone, children }: { tone: "ok" | "excluded" | "bad"; children: ReactNode }) {
  return (
    <span className="tag" data-tone={tone}>
      {children}
    </span>
  );
}

/** Five pips showing recap weight. Decorative on its own; the label carries the meaning. */
export function WeightPips({ value }: { value: number }) {
  return (
    <span className="weight" role="img" aria-label={`Recap weight ${value} of 5`}>
      {[1, 2, 3, 4, 5].map((step) => (
        <i key={step} data-on={step <= value ? "true" : undefined} />
      ))}
    </span>
  );
}

export function Skeleton({ width, height }: { width: string; height?: number }) {
  return <div className="skel" style={{ width, ...(height ? { height: `${height}px` } : {}) }} />;
}

export function SkeletonRows({ rows = 4 }: { rows?: number }) {
  const widths = ["82%", "64%", "74%", "58%", "70%", "48%"];

  return (
    <div className="skel-stack" aria-hidden="true">
      {Array.from({ length: rows }, (_, index) => (
        <div className="skel-row" key={index}>
          <Skeleton width={widths[index % widths.length]!} />
          <Skeleton width="22%" height={9} />
        </div>
      ))}
    </div>
  );
}

export function EmptyState({
  title,
  children,
  action,
}: {
  title: string;
  children: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="empty">
      <h2>{title}</h2>
      <p>{children}</p>
      {action}
    </div>
  );
}

export function ErrorNote({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="callout" data-tone="bad" role="alert">
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
      <span>
        {message}
        {onRetry ? (
          <>
            {" "}
            <button className="link-btn" type="button" onClick={onRetry}>
              Try again
            </button>
          </>
        ) : null}
      </span>
    </div>
  );
}

/** Renders whichever of loading / error / empty applies, otherwise the content. */
export function AsyncSection({
  isLoading,
  error,
  onRetry,
  isEmpty,
  empty,
  skeleton,
  children,
}: {
  isLoading: boolean;
  error: unknown;
  onRetry?: () => void;
  isEmpty?: boolean;
  empty?: ReactNode;
  skeleton?: ReactNode;
  children: ReactNode;
}) {
  if (isLoading) return <>{skeleton ?? <SkeletonRows />}</>;

  if (error) {
    return (
      <ErrorNote
        message={error instanceof Error ? error.message : "Something went wrong."}
        {...(onRetry ? { onRetry } : {})}
      />
    );
  }

  if (isEmpty && empty) return <>{empty}</>;

  return <>{children}</>;
}
