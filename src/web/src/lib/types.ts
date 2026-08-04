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
  skipCertificateVerification: boolean;
}

export interface UpdateSmtpSettingsRequest {
  host?: string;
  port?: number;
  fromAddress?: string;
  username?: string;
  password?: string;
  skipCertificateVerification?: boolean;
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
