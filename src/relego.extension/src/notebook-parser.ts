/**
 * Kindle notebook page parser.
 *
 * The parser reads the user's Kindle Notebook page and turns it into the normalized batch shape the
 * Relego server accepts. The implementation (structural invariants and contract-break diagnostics)
 * lands with the synchronization task.
 */

/** A single highlight parsed from a Kindle notebook page. */
export interface ParsedHighlight {
  /** Provider-stable identifier when the page exposes one, otherwise `null`. */
  readonly externalId: string | null;
  /** Highlight text exactly as shown on the page. */
  readonly text: string;
  /** Reader note attached to the highlight, when present. */
  readonly note: string | null;
  /** Location range as shown on the page, when present. */
  readonly location: string | null;
  /** Highlight colour as shown on the page, when present. */
  readonly color: string | null;
}

/** A book with its highlights, as parsed from a Kindle notebook page. */
export interface ParsedBook {
  /** Provider-stable book key when available, otherwise a title-derived key. */
  readonly externalKey: string;
  /** Book title as shown on the page. */
  readonly title: string;
  /** Book author as shown on the page, when present. */
  readonly author: string | null;
  /** Whether the export-limit truncation warning was seen for this book. */
  readonly truncated: boolean;
  /** Highlights parsed for this book. */
  readonly highlights: readonly ParsedHighlight[];
}

/** Result of parsing a notebook page. */
export interface ParseResult {
  /** Books parsed from the page. */
  readonly books: readonly ParsedBook[];
}
