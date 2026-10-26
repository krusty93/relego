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

import { PROFILE_VERSION, type KindleRegion } from "./profile";

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
  /** Provider-stable book key carried by the page when it exposes one, otherwise the requested key. */
  readonly externalKey: string;
  /**
   * Book title the page shows, or an empty string when it shows none — the live annotations page
   * carries no heading, so the library row that selected the book stays the source of the title.
   */
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
  let missingKeys = 0;

  for (const row of rows) {
    const title = selectText(row.content, TITLE_CANDIDATES);
    if (title === null || title.length === 0) {
      missingTitles++;
      continue;
    }

    // Without the row's own key the book page cannot be addressed: Amazon ignores a title and
    // answers with whichever book the notebook is showing, so every row would import the same
    // highlights.
    const externalKey = bookKeyOf(row.open);
    if (externalKey === null) {
      missingKeys++;
      continue;
    }

    books.push({
      externalKey,
      title,
      author: selectText(row.content, ROW_AUTHOR_CANDIDATES),
      highlightCount: parseCount(attribute(row.open, "data-highlight-count")),
      truncated: elementContent(row.content, SELECTORS.exportLimit) !== null,
    });
  }

  if (missingTitles > 0) {
    return contractBreak("bookTitle", { bookRows: rows.length, missingTitles, titleAnchors: countText(list, TITLE_CANDIDATES) });
  }

  if (missingKeys > 0) {
    return contractBreak("bookKey", { bookRows: rows.length, keyedRows: books.length, missingKeys });
  }

  return { kind: "ok", books };
}

/**
 * Parses a single book's annotations page.
 *
 * The page is not required to name the book: the live annotations page carries no heading, and the
 * library row that selected this book already provides the title and author.
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

  const title = selectText(html, TITLE_CANDIDATES) ?? "";

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
 *
 * An ASIN is ten alphanumerics — `B0…` for most Kindle books, an ISBN for older ones — so the id is
 * matched on that shape rather than on a `B` prefix, and either as the whole id or embedded in it.
 */
function bookKeyOf(openTag: string): string | null {
  const explicit = attribute(openTag, "data-book-key") ?? attribute(openTag, "data-asin");
  if (explicit !== null) return explicit;

  const id = attribute(openTag, "id");
  if (id === null) return null;

  const normalized = id.trim().toUpperCase();
  if (/^[0-9A-Z]{10}$/.test(normalized)) return normalized;

  const embedded = /\b[0-9A-Z]{10}\b/.exec(normalized);
  return embedded === null ? null : embedded[0];
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

/**
 * Character references in page text: a decimal or hexadecimal code point, or a named entity.
 */
const ENTITY_PATTERN = /&(#[0-9]+|#[xX][0-9a-fA-F]+|[a-zA-Z][a-zA-Z0-9]*);/g;

/**
 * Named entities the notebook pages carry, as `name:codePoint` pairs — the HTML 4 names for Latin-1,
 * Greek, punctuation and symbols.
 *
 * Amazon escapes every non-ASCII character it serves, so accented letters and typographic quotes
 * arrive as `&egrave;` or `&rsquo;` and the table is the complete HTML set rather than a sample.
 * Numeric references need no entry.
 */
const NAMED_ENTITY_DATA =
  "nbsp:160,iexcl:161,cent:162,pound:163,curren:164,yen:165,brvbar:166,sect:167,uml:168,copy:169,ordf:170,laquo:171,not:172,shy:173,reg:174,macr:175," +
  "deg:176,plusmn:177,sup2:178,sup3:179,acute:180,micro:181,para:182,middot:183,cedil:184,sup1:185,ordm:186,raquo:187,frac14:188,frac12:189,frac34:190,iquest:191," +
  "Agrave:192,Aacute:193,Acirc:194,Atilde:195,Auml:196,Aring:197,AElig:198,Ccedil:199,Egrave:200,Eacute:201,Ecirc:202,Euml:203,Igrave:204,Iacute:205,Icirc:206,Iuml:207," +
  "ETH:208,Ntilde:209,Ograve:210,Oacute:211,Ocirc:212,Otilde:213,Ouml:214,times:215,Oslash:216,Ugrave:217,Uacute:218,Ucirc:219,Uuml:220,Yacute:221,THORN:222,szlig:223," +
  "agrave:224,aacute:225,acirc:226,atilde:227,auml:228,aring:229,aelig:230,ccedil:231,egrave:232,eacute:233,ecirc:234,euml:235,igrave:236,iacute:237,icirc:238,iuml:239," +
  "eth:240,ntilde:241,ograve:242,oacute:243,ocirc:244,otilde:245,ouml:246,divide:247,oslash:248,ugrave:249,uacute:250,ucirc:251,uuml:252,yacute:253,thorn:254,yuml:255," +
  "Alpha:913,Beta:914,Gamma:915,Delta:916,Epsilon:917,Zeta:918,Eta:919,Theta:920,Iota:921,Kappa:922,Lambda:923,Mu:924,Nu:925,Xi:926,Omicron:927,Pi:928," +
  "Rho:929,Sigma:931,Tau:932,Upsilon:933,Phi:934,Chi:935,Psi:936,Omega:937,alpha:945,beta:946,gamma:947,delta:948,epsilon:949,zeta:950,eta:951,theta:952," +
  "iota:953,kappa:954,lambda:955,mu:956,nu:957,xi:958,omicron:959,pi:960,rho:961,sigmaf:962,sigma:963,tau:964,upsilon:965,phi:966,chi:967,psi:968," +
  "omega:969,quot:34,amp:38,apos:39,lt:60,gt:62,OElig:338,oelig:339,Scaron:352,scaron:353,Yuml:376,fnof:402,circ:710,tilde:732,thetasym:977,upsih:978," +
  "piv:982,ensp:8194,emsp:8195,thinsp:8201,zwnj:8204,zwj:8205,lrm:8206,rlm:8207,ndash:8211,mdash:8212,lsquo:8216,rsquo:8217,sbquo:8218,ldquo:8220,rdquo:8221,bdquo:8222," +
  "dagger:8224,Dagger:8225,bull:8226,hellip:8230,permil:8240,prime:8242,Prime:8243,lsaquo:8249," +
  "rsaquo:8250,oline:8254,frasl:8260,euro:8364,image:8465,weierp:8472,real:8476,trade:8482,alefsym:8501,larr:8592,uarr:8593,rarr:8594,darr:8595,harr:8596,crarr:8629,lArr:8656," +
  "uArr:8657,rArr:8658,dArr:8659,hArr:8660,forall:8704,part:8706,exist:8707,empty:8709,nabla:8711,isin:8712,notin:8713,ni:8715,prod:8719,sum:8721,minus:8722,lowast:8727," +
  "radic:8730,prop:8733,infin:8734,ang:8736,and:8743,or:8744,cap:8745,cup:8746,int:8747,there4:8756,sim:8764,cong:8773,asymp:8776,ne:8800,equiv:8801,le:8804," +
  "ge:8805,sub:8834,sup:8835,nsub:8836,sube:8838,supe:8839,oplus:8853,otimes:8855,perp:8869,sdot:8901,lceil:8968,rceil:8969,lfloor:8970,rfloor:8971,lang:9001,rang:9002," +
  "loz:9674,spades:9824,clubs:9827,hearts:9829,diams:9830";

/** Named entities, by name. */
const NAMED_ENTITIES = new Map<string, number>(
  NAMED_ENTITY_DATA.split(",").map((pair) => {
    const separator = pair.indexOf(":");

    return [pair.slice(0, separator), Number.parseInt(pair.slice(separator + 1), 10)];
  }),
);

/** Whether a code point can appear in a JavaScript string, as opposed to a lone surrogate. */
function isScalarValue(codePoint: number): boolean {
  const surrogate = codePoint >= 0xd800 && codePoint <= 0xdfff;

  return Number.isInteger(codePoint) && codePoint > 0 && codePoint <= 0x10ffff && !surrogate;
}

/**
 * Decodes character references in a single pass.
 *
 * One pass is what keeps `&amp;igrave;` the literal text `&igrave;` rather than an accent, because a
 * replacement is never rescanned. Text that is already Unicode — Japanese, Cyrillic, emoji — has no
 * reference to match and passes through unchanged, and an unknown name is left exactly as it came.
 */
function decodeEntities(value: string): string {
  return value.replace(ENTITY_PATTERN, (reference: string, body: string) => {
    if (body.startsWith("#")) {
      const hexadecimal = body[1] === "x" || body[1] === "X";
      const codePoint = hexadecimal
        ? Number.parseInt(body.slice(2), 16)
        : Number.parseInt(body.slice(1), 10);

      return isScalarValue(codePoint) ? String.fromCodePoint(codePoint) : reference;
    }

    const named = NAMED_ENTITIES.get(body);

    return named === undefined ? reference : String.fromCodePoint(named);
  });
}
