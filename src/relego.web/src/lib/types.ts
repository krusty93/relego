export interface StatusResponse {
  totalHighlights: number;
  totalBooks: number;
  totalAuthors: number;
  excludedHighlights: number;
  excludedBooks: number;
  excludedAuthors: number;
  nextRecap: string | null;
  lastRecapStatus: string | null;
  lastRecapError: string | null;
  kindleEmailConfigured: boolean;
  deliveryEmailConfigured: boolean;
  serverVersion: string | null;
}

export interface BookItem {
  id: number;
  title: string;
  authorId: number;
  authorName: string;
  highlightCount: number;
  excludedHighlightCount: number;
  excluded: boolean;
  authorExcluded: boolean;
}

export interface BooksResponse {
  total: number;
  page: number;
  pageSize: number;
  items: BookItem[];
}

export interface HighlightItem {
  id: number;
  bookId: number;
  authorId: number;
  text: string;
  bookTitle: string;
  authorName: string;
}

export interface HighlightsResponse {
  total: number;
  page: number;
  pageSize: number;
  items: HighlightItem[];
}

export interface WeightedHighlight {
  id: number;
  text: string;
  bookTitle: string;
  weight: number;
}

export interface ExcludedHighlight {
  id: number;
  text: string;
  bookTitle: string;
}

export interface ExclusionsResponse {
  highlights: ExcludedHighlight[];
  books: { id: number; title: string; authorName: string; highlightCount: number }[];
  authors: { id: number; name: string; bookCount: number }[];
}

export interface SettingsResponse {
  schedule: string;
  deliveryDay: string | null;
  deliveryTime: string;
  count: number;
  kindleEmail: string;
  timezone: string;
  deliveryEmail: string | null;
}

export interface UpdateSettingsRequest {
  schedule?: string;
  deliveryDay?: string | null;
  deliveryTime?: string;
  count?: number;
  kindleEmail?: string;
  timezone?: string;
  deliveryEmail?: string;
}

export interface SmtpSettingsResponse {
  host: string;
  port: number;
  fromAddress: string;
  username: string;
  passwordSet: boolean;
  source: "database" | "environment" | "default";
  updatedAt: string | null;
}

export interface UpdateSmtpSettingsRequest {
  host?: string;
  port?: number;
  fromAddress?: string;
  username?: string;
  password?: string;
}

export interface SmtpTestResponse {
  success: boolean;
  message: string;
}

export interface ImportResponse {
  source: string;
  sourceName: string;
  fileName: string;
  booksParsed: number;
  highlightsParsed: number;
  entriesProcessed: number;
  duplicatesInFile: number;
  newHighlights: number;
  duplicateHighlights: number;
  newBooks: number;
  newAuthors: number;
}

export interface RecapHistoryItem {
  id: number;
  scheduledFor: string;
  status: string;
  attemptCount: number;
  errorMessage: string | null;
  deliveredAt: string | null;
  createdAt: string;
}

export interface RecapHistoryResponse {
  items: RecapHistoryItem[];
}

export interface SyncProviderCapabilities {
  pushIngest: boolean;
  fullResync: boolean;
  progressReporting: boolean;
}

export interface SyncProviderSummary {
  providerId: string;
  displayName: string;
  capabilities: SyncProviderCapabilities;
  inactivityReminderDays: number;
  disclosureVersion: string;
}

export interface CreateSyncConnectionResponse {
  providerId: string;
  /** One-time pairing code. The server returns it exactly once and stores only its hash. */
  token: string;
}

export interface SyncRegion {
  id: string;
  displayName: string;
  host: string;
}

/** The regions a connection may read, with the server's own disclosure about them. */
export interface SyncRegionsResponse {
  regions: SyncRegion[];
  disclosure: string;
}

export interface SyncSchedule {
  intervalMinutes: number;
  allowedIntervalMinutes: number[];
  nextRunAt: string | null;
  applied: boolean;
  pendingCommand: string;
}

/** The stored frequency after a change, with the extension-reported schedule it will be applied to. */
export interface SyncScheduleChangeResponse {
  intervalMinutes: number;
  applied: boolean;
  nextRunAt: string | null;
}

export interface SyncCommandResponse {
  providerId: string;
  /** Command waiting for the extension's next heartbeat. */
  pendingCommand: string;
}

export interface SyncConnection {
  state: string;
  connectedAt: string | null;
  lastHeartbeatAt: string | null;
  browserReporting: boolean;
  region: SyncRegion | null;
}

export interface SyncJob {
  id: number;
  mode: string;
  status: string;
  phase: string | null;
  booksTotal: number | null;
  booksDone: number | null;
  highlightsSeen: number;
  highlightsNew: number;
  highlightsDuplicate: number;
  truncatedBooks: number;
  startedAt: string | null;
  endedAt: string | null;
}

export interface SyncFailureDiagnostics {
  profileVersion: string | null;
  check: string | null;
  counts: Record<string, number>;
}

export interface SyncFailure {
  code: string;
  detail: string | null;
  recoverySteps: string[];
  diagnostics: SyncFailureDiagnostics | null;
  occurredAt: string | null;
}

export interface SyncDisclosureItem {
  kind: string;
  title: string;
  body: string;
}

export interface SyncDisclosure {
  version: string;
  items: SyncDisclosureItem[];
}

export interface SyncReminder {
  id: number;
  quietSince: string;
  thresholdDays: number;
  createdAt: string | null;
  dismissed: boolean;
}

export interface SyncStatusResponse {
  providerId: string;
  status: string;
  connection: SyncConnection;
  schedule: SyncSchedule;
  currentJob: SyncJob | null;
  lastCompletedJob: SyncJob | null;
  failure: SyncFailure | null;
  reminder: SyncReminder | null;
  disclosure: SyncDisclosure;
  prerequisites: string[];
}
