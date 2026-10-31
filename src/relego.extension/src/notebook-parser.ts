/**
 * Kindle notebook page parser.
 *
 * The parser reads the user's Kindle Notebook page and turns it into the normalized batch shape the
 * Relego server accepts. It runs in the extension service worker, where no DOM is available, so it
 * is a small profile-driven HTML scanner: every lookup is anchored on a stable id or class recorded
 * in `SELECTORS`, and a missing anchor is reported as a contract break instead of an empty result.
 *
 * Nothing from the page is ever returned in a failure: diagnostics carry the profile version, the
 * failing selector id and counts only.
 */

import { PROFILE_VERSION, normalizeBookKey, type KindleRegion } from "./profile";

/**
 * Stable anchors of the notebook parse profile. These are the identifiers the failure diagnostics
 * name, so they must stay in sync with the fixtures under `tests/fixtures/<regionId>/`.
 */
export const SELECTORS = {
  libraryList: { tag: "div", marker: 'id="kp-notebook-library"' },
  libraryEmpty: { tag: "div", marker: 'id="kp-notebook-empty"' },
  bookRow: { tag: "div", marker: 'class="kp-notebook-library-each-book"' },
  bookContainer: { tag: "div", marker: 'id="kp-notebook-main"' },
  bookTitle: { tag: "h2", marker: 'id="kp-notebook-book-title"' },
  bookAuthor: { tag: "p", marker: 'id="kp-notebook-book-author"' },
  annotations: { tag: "div", marker: 'id="kp-notebook-annotations"' },
  annotation: { tag: "div", marker: 'class="kp-notebook-annotation"' },
  highlightText: { tag: "span", marker: 'id="highlight"' },
  annotationNote: { tag: "span", marker: 'id="annotationNote"' },
  annotationLocation: { tag: "span", marker: 'id="kp-annotation-location"' },
  exportLimit: { tag: "div", marker: 'id="kp-notebook-export-limit"' },
  signInForm: { tag: "form", marker: 'id="ap_signin_form"' },
} as const;

/** A book as listed on the notebook library page. */
export interface LibraryBook {
  /** Provider-stable book key when the row exposes one, otherwise a title-derived key. */
  readonly externalKey: string;
  /** Book title as shown on the page. */
  readonly title: string;
  /** Book author as shown on the page, when present. */
  readonly author: string | null;
  /** Highlight count the library row advertises, or `null` when it does not. */
  readonly highlightCount: number | null;
  /** Whether the export-limit truncation warning was seen for this book. */
  readonly truncated: boolean;
}

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

/** Sanitized diagnostics for a `provider_contract_changed` failure. */
export interface ContractDiagnostics {
  /** Parse-profile version that produced the failure. */
  readonly profileVersion: string;
  /** Selector id that did not match, or the invariant that was violated. */
  readonly check: string;
  /** Counts observed while parsing; never page content. */
  readonly counts: Record<string, number>;
}

/** Outcome of parsing a notebook page. */
export type ParseOutcome =
  | { readonly kind: "ok"; readonly result: ParseResult }
  | { readonly kind: "sign_in" }
  | { readonly kind: "contract_changed"; readonly diagnostics: ContractDiagnostics };

/** Outcome of parsing the notebook library page. */
export type LibraryOutcome =
  | { readonly kind: "ok"; readonly books: readonly LibraryBook[] }
  | { readonly kind: "sign_in" }
  | { readonly kind: "contract_changed"; readonly diagnostics: ContractDiagnostics };

/**
 * Parses the notebook library page into the book list.
 *
 * @param html Page HTML as served to the user's own authenticated session.
 * @param region Region whose notebook host was read.
 * @returns The parsed book list, a sign-in signal, or a contract break.
 */
export function parseLibrary(html: string, region: KindleRegion): LibraryOutcome {
  if (isSignInPage(html, region)) return { kind: "sign_in" };

  const list = elementContent(html, SELECTORS.libraryList);
  if (list === null) return contractBreak("libraryList", {});

  const rows = findElements(list, SELECTORS.bookRow);
  if (rows.length === 0) {
    // An empty library is legitimate only when the page says so explicitly; otherwise the row
    // selector no longer matches and the run must not look like a successful empty sync.
    return elementContent(list, SELECTORS.libraryEmpty) !== null
      ? { kind: "ok", books: [] }
      : contractBreak("bookRow", { bookRows: 0 });
  }

  const books: LibraryBook[] = [];
  let missingTitles = 0;

  for (const row of rows) {
    const title = elementText(row.content, SELECTORS.bookTitle);
    if (title === null || title.length === 0) {
      missingTitles++;
      continue;
    }

    books.push({
      externalKey: attribute(row.open, "data-book-key") ?? normalizeBookKey(title),
      title,
      author: elementText(row.content, SELECTORS.bookAuthor),
      highlightCount: parseCount(attribute(row.open, "data-highlight-count")),
      truncated: elementContent(row.content, SELECTORS.exportLimit) !== null,
    });
  }

  if (missingTitles > 0) {
    return contractBreak("bookTitle", { bookRows: rows.length, missingTitles });
  }

  return { kind: "ok", books };
}

/**
 * Parses a single book's annotations page.
 *
 * @param html Page HTML as served to the user's own authenticated session.
 * @param region Region whose notebook host was read.
 * @param fallbackKey Book key from the library row, used when the page does not expose one.
 * @returns The parsed book, a sign-in signal, or a contract break.
 */
export function parseBookPage(html: string, region: KindleRegion, fallbackKey: string): ParseOutcome {
  if (isSignInPage(html, region)) return { kind: "sign_in" };

  const annotations = elementContent(html, SELECTORS.annotations);
  if (annotations === null) return contractBreak("annotations", {});

  const title = elementText(html, SELECTORS.bookTitle);
  if (title === null || title.length === 0) return contractBreak("bookTitle", { annotations: countAnnotations(annotations) });

  const truncated = elementContent(annotations, SELECTORS.exportLimit) !== null;
  const blocks = findElements(annotations, SELECTORS.annotation);
  const highlights: ParsedHighlight[] = [];
  let missingText = 0;

  for (const block of blocks) {
    const text = elementText(block.content, SELECTORS.highlightText);
    if (text === null || text.length === 0) {
      missingText++;
      continue;
    }

    highlights.push({
      externalId: attribute(block.open, "data-highlight-id"),
      text,
      note: elementText(block.content, SELECTORS.annotationNote),
      location: elementText(block.content, SELECTORS.annotationLocation),
      color: attribute(block.open, "data-color"),
    });
  }

  if (missingText > 0) {
    return contractBreak("highlightText", { annotations: blocks.length, missingText });
  }

  return {
    kind: "ok",
    result: {
      books: [
        {
          externalKey: attribute(elementOpen(html, SELECTORS.bookContainer) ?? "", "data-book-key") ?? fallbackKey,
          title,
          author: elementText(html, SELECTORS.bookAuthor),
          truncated,
          highlights,
        },
      ],
    },
  };
}

/** Reports whether the page is an Amazon sign-in surface rather than the notebook. */
export function isSignInPage(html: string, region: KindleRegion): boolean {
  if (elementContent(html, SELECTORS.signInForm) !== null) return true;

  const action = attribute(html, "action");
  if (action !== null && /\/ap\/signin|\/signin/i.test(action)) return true;

  const canonical = attribute(html, "data-signin-host");
  return canonical !== null && region.signInHostPatterns.includes(canonical);
}

function contractBreak(check: string, counts: Record<string, number>): {
  readonly kind: "contract_changed";
  readonly diagnostics: ContractDiagnostics;
} {
  return {
    kind: "contract_changed",
    diagnostics: { profileVersion: PROFILE_VERSION, check, counts },
  };
}

function countAnnotations(html: string): number {
  return findElements(html, SELECTORS.annotation).length;
}

function parseCount(value: string | null): number | null {
  if (value === null) return null;

  const parsed = Number.parseInt(value, 10);
  return Number.isNaN(parsed) || parsed < 0 ? null : parsed;
}

interface Selector {
  readonly tag: string;
  readonly marker: string;
}

interface Element {
  readonly open: string;
  readonly content: string;
}

/**
 * Returns every element of `selector.tag` whose opening tag carries the selector marker, honouring
 * nesting of the same tag name. Each element exposes its opening tag (for attribute lookups) and
 * the markup between its tags.
 */
function findElements(html: string, selector: Selector): Element[] {
  const results: Element[] = [];
  const openPattern = new RegExp(`<${selector.tag}\\b[^>]*>`, "gi");
  let match: RegExpExecArray | null;

  while ((match = openPattern.exec(html)) !== null) {
    const openTag = match[0];
    if (!openTag.includes(selector.marker)) continue;

    const contentStart = match.index + openTag.length;
    if (openTag.endsWith("/>")) {
      results.push({ open: openTag, content: "" });
      continue;
    }

    const contentEnd = findCloseIndex(html, selector.tag, contentStart);
    if (contentEnd === null) continue;

    results.push({ open: openTag, content: html.slice(contentStart, contentEnd) });
    openPattern.lastIndex = contentEnd;
  }

  return results;
}

/** Index of the `</tag>` that closes the element opened just before `from`, or `null`. */
function findCloseIndex(html: string, tag: string, from: number): number | null {
  const pattern = new RegExp(`<(/?)${tag}\\b[^>]*>`, "gi");
  pattern.lastIndex = from;
  let depth = 1;
  let match: RegExpExecArray | null;

  while ((match = pattern.exec(html)) !== null) {
    if (match[0].endsWith("/>")) continue;

    depth += match[1] === "/" ? -1 : 1;
    if (depth === 0) return match.index;
  }

  return null;
}

/** Content of the first matching element, or `null` when the selector does not match. */
function elementContent(html: string, selector: Selector): string | null {
  const found = findElements(html, selector);
  return found.length === 0 ? null : (found[0] as Element).content;
}

/** Opening tag of the first matching element, or `null` when the selector does not match. */
function elementOpen(html: string, selector: Selector): string | null {
  const found = findElements(html, selector);
  return found.length === 0 ? null : (found[0] as Element).open;
}

/** Visible text of the first matching element, or `null` when it does not match or is blank. */
function elementText(html: string, selector: Selector): string | null {
  const content = elementContent(html, selector);
  if (content === null) return null;

  const text = textOf(content);
  return text.length === 0 ? null : text;
}

/** Value of the first `name="value"` attribute in the fragment, or `null`. */
function attribute(html: string, name: string): string | null {
  const pattern = new RegExp(`\\b${name}\\s*=\\s*"([^"]*)"`, "i");
  const match = pattern.exec(html);
  return match === null ? null : decodeEntities((match[1] as string).trim());
}

/**
 * Strips tags from a fragment, repeating the replacement until the fragment stops changing.
 *
 * Removing a whole tag in one pass is only safe while the removal cannot join the text on both
 * sides of it back into a tag, so the replacement runs to a fixed point instead of assuming it.
 */
function stripTags(html: string): string {
  let stripped = html;
  let previous: string;

  do {
    previous = stripped;
    stripped = stripped.replace(/<[^>]*>/g, "");
  } while (stripped !== previous);

  return stripped;
}

/** Strips tags from a fragment and decodes entities. */
function textOf(html: string): string {
  return decodeEntities(stripTags(html)).replace(/\s+/g, " ").trim();
}

function decodeEntities(value: string): string {
  return value
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&#0?39;/g, "'")
    .replace(/&nbsp;/g, " ")
    .replace(/&#(\d+);/g, (_match, code: string) => String.fromCodePoint(Number.parseInt(code, 10)))
    .replace(/&amp;/g, "&");
}
