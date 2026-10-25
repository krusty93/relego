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
 *
 * Matching is structural, never literal: an anchor is an id or a class *token* read from the parsed
 * attributes of an opening tag, so an extra class, a different quote style or a reordered attribute
 * list does not break the profile. `tag` is only a narrowing hint; omit it when the page is free to
 * change the element name.
 */
export const SELECTORS = {
  libraryList: { tag: "div", id: "kp-notebook-library" },
  libraryEmpty: { tag: "div", id: "kp-notebook-empty" },
  bookRow: { className: "kp-notebook-library-each-book" },
  bookContainer: { tag: "div", id: "kp-notebook-main" },
  bookTitle: { tag: "h2", id: "kp-notebook-book-title" },
  bookAuthor: { tag: "p", id: "kp-notebook-book-author" },
  annotations: { tag: "div", id: "kp-notebook-annotations" },
  annotation: { className: "kp-notebook-annotation" },
  highlightText: { tag: "span", id: "highlight" },
  annotationNote: { tag: "span", id: "annotationNote" },
  annotationLocation: { tag: "span", id: "kp-annotation-location" },
  exportLimit: { tag: "div", id: "kp-notebook-export-limit" },
  signInForm: { tag: "form", id: "ap_signin_form" },
} as const satisfies Record<string, Selector>;

/**
 * Tolerated alternatives, tried in order after the anchor itself. A contract break only happens when
 * every candidate misses, which is what separates "Amazon renamed a wrapper" from "Amazon replaced
 * the page".
 */
const TITLE_CANDIDATES: readonly Selector[] = [
  SELECTORS.bookTitle,
  { tag: "h2" },
  { className: "kp-notebook-book-title" },
];

const AUTHOR_CANDIDATES: readonly Selector[] = [SELECTORS.bookAuthor, { className: "kp-notebook-book-author" }];

/**
 * A library row is a small card, so its author may be an unmarked paragraph. The book page is not:
 * there a bare `<p>` could be highlight prose, so it keeps the id/class anchors only.
 */
const ROW_AUTHOR_CANDIDATES: readonly Selector[] = [...AUTHOR_CANDIDATES, { tag: "p" }];

const EMPTY_CANDIDATES: readonly Selector[] = [SELECTORS.libraryEmpty, { className: "kp-notebook-empty" }];

const HIGHLIGHT_CANDIDATES: readonly Selector[] = [
  SELECTORS.highlightText,
  { tag: "span", idPrefix: "highlight-" },
  { tag: "span", className: "highlight" },
];

const ANNOTATION_CANDIDATES: readonly Selector[] = [
  SELECTORS.annotation,
  { idPrefix: "annotation-" },
  { className: "kp-notebook-highlight" },
];

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
  if (list === null) return contractBreak("libraryList", { lists: findElements(html, SELECTORS.libraryList).length });

  const rows = findElements(list, SELECTORS.bookRow);
  if (rows.length === 0) {
    // An empty library is legitimate only when the page says so explicitly; otherwise the row
    // selector no longer matches and the run must not look like a successful empty sync.
    return selectFirst(list, EMPTY_CANDIDATES) !== null
      ? { kind: "ok", books: [] }
      : contractBreak("bookRow", describeList(list));
  }

  const books: LibraryBook[] = [];
  let missingTitles = 0;

  for (const row of rows) {
    const title = selectText(row.content, TITLE_CANDIDATES);
    if (title === null || title.length === 0) {
      missingTitles++;
      continue;
    }

    books.push({
      externalKey: bookKeyOf(row.open) ?? normalizeBookKey(title),
      title,
      author: selectText(row.content, ROW_AUTHOR_CANDIDATES),
      highlightCount: parseCount(attribute(row.open, "data-highlight-count")),
      truncated: elementContent(row.content, SELECTORS.exportLimit) !== null,
    });
  }

  if (missingTitles > 0) {
    return contractBreak("bookTitle", { bookRows: rows.length, missingTitles, titleAnchors: countText(list, TITLE_CANDIDATES) });
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

  const title = selectText(html, TITLE_CANDIDATES);
  if (title === null || title.length === 0) {
    return contractBreak("bookTitle", { annotations: countAnnotations(annotations), headings: findElements(html, { tag: "h2" }).length });
  }

  const truncated = elementContent(annotations, SELECTORS.exportLimit) !== null;
  const blocks = selectAll(annotations, ANNOTATION_CANDIDATES);
  const highlights: ParsedHighlight[] = [];
  let missingText = 0;

  for (const block of blocks) {
    const text = selectText(block.content, HIGHLIGHT_CANDIDATES);
    if (text === null || text.length === 0) {
      missingText++;
      continue;
    }

    highlights.push({
      externalId: attribute(block.open, "data-highlight-id") ?? annotationId(block.open),
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
          externalKey: bookKeyOf(elementOpen(html, SELECTORS.bookContainer) ?? "") ?? fallbackKey,
          title,
          author: selectText(html, AUTHOR_CANDIDATES),
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
  return selectAll(html, ANNOTATION_CANDIDATES).length;
}

function countText(html: string, candidates: readonly Selector[]): number {
  return selectAll(html, candidates).filter((element) => textOf(element.content).length > 0).length;
}

function parseCount(value: string | null): number | null {
  if (value === null) return null;

  const parsed = Number.parseInt(value, 10);
  return Number.isNaN(parsed) || parsed < 0 ? null : parsed;
}

/**
 * Structural anchor: an id, an id prefix or a class token, optionally narrowed to one tag name.
 * Every field is read from the parsed attributes of the opening tag, so matching survives added
 * classes, single quotes and reordered attributes.
 */
interface Selector {
  readonly tag?: string;
  readonly id?: string;
  readonly idPrefix?: string;
  readonly className?: string;
}

interface Element {
  readonly open: string;
  readonly content: string;
}

/** Attributes of an opening tag, keyed by lowercased name, with either quote style or none. */
function attributesOf(openTag: string): Map<string, string> {
  const attributes = new Map<string, string>();
  const pattern = /([a-zA-Z_:][-\w:.]*)\s*(?:=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'`=<>]+)))?/g;
  const body = openTag.replace(/^<\s*\/?\s*[a-zA-Z][\w:-]*/, "");

  let match: RegExpExecArray | null;
  while ((match = pattern.exec(body)) !== null) {
    const name = (match[1] as string).toLowerCase();
    if (attributes.has(name)) continue;

    attributes.set(name, match[2] ?? match[3] ?? match[4] ?? "");
  }

  return attributes;
}

/** Whether the opening tag satisfies every constraint the selector states. */
function matches(openTag: string, selector: Selector): boolean {
  const attributes = attributesOf(openTag);
  const id = attributes.get("id") ?? null;

  if (selector.id !== undefined && id !== selector.id) return false;
  if (selector.idPrefix !== undefined && (id === null || !id.startsWith(selector.idPrefix))) return false;

  if (selector.className !== undefined) {
    const tokens = (attributes.get("class") ?? "").split(/\s+/).filter((token) => token.length > 0);
    if (!tokens.includes(selector.className)) return false;
  }

  return true;
}

/** Tag name of an opening tag, or `null` when the fragment is not one. */
function tagOf(openTag: string): string | null {
  const match = /^<\s*([a-zA-Z][\w:-]*)/.exec(openTag);
  return match === null ? null : (match[1] as string);
}

/**
 * Returns every element the selector matches, honouring nesting of the same tag name. Each element
 * exposes its opening tag (for attribute lookups) and the markup between its tags.
 */
function findElements(html: string, selector: Selector): Element[] {
  const results: Element[] = [];
  const openPattern = selector.tag === undefined ? /<[a-zA-Z][^>]*>/g : new RegExp(`<${selector.tag}\\b[^>]*>`, "gi");
  let match: RegExpExecArray | null;

  while ((match = openPattern.exec(html)) !== null) {
    const openTag = match[0];
    if (!matches(openTag, selector)) continue;

    const tag = tagOf(openTag);
    const contentStart = match.index + openTag.length;

    if (tag === null || openTag.endsWith("/>") || VOID_TAGS.has(tag.toLowerCase())) {
      results.push({ open: openTag, content: "" });
      continue;
    }

    const contentEnd = findCloseIndex(html, tag, contentStart);
    if (contentEnd === null) continue;

    results.push({ open: openTag, content: html.slice(contentStart, contentEnd) });
    openPattern.lastIndex = contentEnd;
  }

  return results;
}

const VOID_TAGS = new Set(["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"]);

/** Every element matched by any candidate, deduplicated, in document order. */
function selectAll(html: string, candidates: readonly Selector[]): Element[] {
  const seen = new Set<string>();
  const results: Element[] = [];

  for (const candidate of candidates) {
    for (const element of findElements(html, candidate)) {
      const key = `${element.open}\u0000${element.content}`;
      if (seen.has(key)) continue;

      seen.add(key);
      results.push(element);
    }
  }

  return results;
}

/** First element matched by any candidate, or `null`. */
function selectFirst(html: string, candidates: readonly Selector[]): Element | null {
  for (const candidate of candidates) {
    const found = findElements(html, candidate);
    if (found.length > 0) return found[0] as Element;
  }

  return null;
}

/** Visible text of the first candidate that matches with non-blank text, or `null`. */
function selectText(html: string, candidates: readonly Selector[]): string | null {
  for (const candidate of candidates) {
    const text = elementText(html, candidate);
    if (text !== null) return text;
  }

  return null;
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
  const pattern = new RegExp(`\\b${name}\\s*=\\s*(?:"([^"]*)"|'([^']*)'|([^\\s"'\`=<>]+))`, "i");
  const match = pattern.exec(html);
  if (match === null) return null;

  const value = (match[1] ?? match[2] ?? match[3] ?? "").trim();
  return value.length === 0 ? null : decodeEntities(value);
}

/**
 * Provider-stable book key carried by a row or container: an explicit key or ASIN attribute first,
 * then an ASIN embedded in the element id, which is how the live notebook page marks a row.
 */
function bookKeyOf(openTag: string): string | null {
  const explicit = attribute(openTag, "data-book-key") ?? attribute(openTag, "data-asin");
  if (explicit !== null) return explicit;

  const id = attribute(openTag, "id");
  if (id === null) return null;

  const asin = /\bB[0-9A-Z]{9}\b/.exec(id.toUpperCase());
  return asin === null ? null : asin[0];
}

/** Highlight id carried by an `annotation-…` element id, or `null`. */
function annotationId(openTag: string): string | null {
  const id = attribute(openTag, "id");
  if (id === null || !id.startsWith("annotation-")) return null;

  const suffix = id.slice("annotation-".length);
  return suffix.length === 0 ? null : suffix;
}

/**
 * Text-free fingerprint of a library list whose rows no longer match. It separates an empty
 * document from a renamed row wrapper without ever carrying page content.
 */
function describeList(list: string): Record<string, number> {
  const elements = findElements(list, {});
  const keyed = elements.filter((element) => {
    const attributes = attributesOf(element.open);
    return attributes.has("data-book-key") || attributes.has("data-asin");
  });

  return {
    bookRows: 0,
    listElements: elements.length,
    keyedRows: keyed.length,
    titleAnchors: countText(list, TITLE_CANDIDATES),
  };
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
