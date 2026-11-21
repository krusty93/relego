const numberFormat = new Intl.NumberFormat();

export function formatCount(value: number): string {
  return numberFormat.format(value);
}

export function plural(
  count: number,
  singular: string,
  pluralForm = `${singular}s`,
): string {
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
  return minutes < 60
    ? plural(minutes, "minute")
    : plural(minutes / 60, "hour");
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
export function secondsSince(
  iso: string | null | undefined,
  nowMs: number,
): number | null {
  if (!iso) return null;

  const then = Date.parse(iso);
  if (Number.isNaN(then)) return null;

  return Math.max(0, Math.round((nowMs - then) / 1000));
}

export function capitalise(value: string): string {
  return value.length === 0 ? value : value[0]!.toUpperCase() + value.slice(1);
}

/**
 * What a heading level means to a reader: level 1 titles a chapter, 2 a section, 3 a subsection.
 * Levels outside 1–3 cannot come from a marker, so they read as the generic "Section".
 */
export function describeHeadingLevel(level: number): string {
  if (level <= 1) return "Chapter";
  return level === 2 ? "Section" : "Subsection";
}

/** A chapter path root to leaf as one muted line: "Part One › Notes". */
export function describeChapterPath(path: string[]): string {
  return path.join(" › ");
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
  if (Number.isNaN(date.getTime()))
    return { text: "—", zone: timeZone ?? localZone };

  const options: Intl.DateTimeFormatOptions = {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  };

  if (timeZone) {
    try {
      return {
        text: date.toLocaleString(undefined, { ...options, timeZone }),
        zone: timeZone,
      };
    } catch {
      // Unknown zone identifier — fall through to the browser's own zone.
    }
  }

  return { text: date.toLocaleString(undefined, options), zone: localZone };
}

export function formatDate(iso: string | null | undefined): string {
  return dateText(iso) ?? "—";
}

/** The date `iso` names at day precision, or `null` when it is missing or unreadable. */
function dateText(iso: string | null | undefined): string | null {
  if (!iso) return null;

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return null;

  return date.toLocaleDateString(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

/** What the UI says where a source reported nothing: never blank, never a dash. */
export const UNKNOWN = "Unknown";

/** The fallback for a book whose source reported no author (FR-008). */
export const UNKNOWN_AUTHOR = "Unknown author";

/**
 * The reading date the source reported, or `Unknown`. Never the import time: `addedOn` is the
 * source's own date and `created_at` is when Relego learned about the annotation (FR-007).
 */
export function formatReadingDate(iso: string | null | undefined): string {
  return dateText(iso) ?? UNKNOWN;
}

/** The reading position exactly as the source reported it, or `Unknown` (FR-006). */
export function formatReadingPosition(
  position: string | null | undefined,
): string {
  const reported = position?.trim();

  return reported ? reported : UNKNOWN;
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
