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

/** "15 minutes" … "24 hours" for the ten routine-sync frequencies. */
export function describeFrequency(minutes: number): string {
  return minutes < 60 ? plural(minutes, "minute") : plural(minutes / 60, "hour");
}

/**
 * A coarse duration for progress copy: "8 s", "1 min 20 s", "1 h 5 min".
 *
 * Seconds are dropped once the span is an hour or more, and a whole number of minutes stays "2 min"
 * rather than "2 min 0 s".
 */
export function formatDuration(totalSeconds: number): string {
  const seconds = Math.max(0, Math.round(totalSeconds));
  if (seconds < 60) return `${seconds} s`;

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    const rest = seconds % 60;

    return rest === 0 ? `${minutes} min` : `${minutes} min ${rest} s`;
  }

  const hours = Math.floor(minutes / 60);
  const restMinutes = minutes % 60;

  return restMinutes === 0 ? `${hours} h` : `${hours} h ${restMinutes} min`;
}

/**
 * Whole seconds between a past timestamp and `nowMs`, or `null` when the timestamp is missing or
 * unreadable. A timestamp in the future reads as zero rather than as a negative age.
 */
export function secondsSince(iso: string | null | undefined, nowMs: number): number | null {
  if (!iso) return null;

  const then = Date.parse(iso);
  if (Number.isNaN(then)) return null;

  return Math.max(0, Math.round((nowMs - then) / 1000));
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
