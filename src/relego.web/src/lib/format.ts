const numberFormat = new Intl.NumberFormat();

export function formatCount(value: number): string {
  return numberFormat.format(value);
}

export function plural(count: number, singular: string, pluralForm = `${singular}s`): string {
  return `${formatCount(count)} ${count === 1 ? singular : pluralForm}`;
}

/** "Sun 18:00" style summary of the recap cadence. */
export function describeSchedule(
  schedule: string,
  deliveryDay: string | null,
  deliveryTime: string,
): string {
  if (schedule === "daily") return `Every day at ${deliveryTime}`;

  const day = deliveryDay ? capitalise(deliveryDay) : "Sunday";
  return `Every ${day} at ${deliveryTime}`;
}

export function capitalise(value: string): string {
  return value.length === 0 ? value : value[0]!.toUpperCase() + value.slice(1);
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";

  return date.toLocaleString(undefined, {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * The schedule is configured in the server's timezone, so the next-recap time
 * has to be rendered in that zone too — otherwise a reader in another zone sees
 * their own local clock time labelled with the server's zone name.
 * Returns the zone the text is actually expressed in so the label can't drift.
 */
export function formatDateTimeInZone(
  iso: string | null | undefined,
  timeZone: string | null | undefined,
): { text: string; zone: string } {
  const localZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  if (!iso) return { text: "—", zone: timeZone ?? localZone };

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return { text: "—", zone: timeZone ?? localZone };

  const options: Intl.DateTimeFormatOptions = {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  };

  if (timeZone) {
    try {
      return { text: date.toLocaleString(undefined, { ...options, timeZone }), zone: timeZone };
    } catch {
      // Unknown zone identifier — fall through to the browser's own zone.
    }
  }

  return { text: date.toLocaleString(undefined, options), zone: localZone };
}

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";

  return date.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export const DAYS = [
  "monday",
  "tuesday",
  "wednesday",
  "thursday",
  "friday",
  "saturday",
  "sunday",
] as const;
