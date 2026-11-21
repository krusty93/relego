import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { expect, test } from "@playwright/test";
import { API_URL } from "../playwright.config";

/**
 * SC-016: what the page shows for an annotation is what the API reports for it. Every assertion
 * below reads the payload the server returns and holds the DOM to it, so this spec fails if the
 * two ever describe the same annotation differently.
 *
 * The shared fixtures declare no markers, so this journey imports clippings of its own: a chapter,
 * a section and a subsection, two tags, a reader note and a two-fragment passage.
 */
const CLIPPINGS = fileURLToPath(
  new URL(
    "../../Relego.Tests/Fixtures/annotation-vocabulary.txt",
    import.meta.url,
  ),
);
const BOOK_TITLE = "Relego Annotation Vocabulary";

type Annotation = {
  id: number;
  bookId: number;
  text: string;
  note: string | null;
  tags: string[];
  headingLevel: number | null;
  chapterPath: string[];
  stitchGroupId: number | null;
  stitchIndex: number | null;
  position: string | null;
};

type Chapter = {
  id: number;
  title: string;
  level: number;
  parentId: number | null;
  annotationCount: number;
};

type ChapterList = {
  hasTableOfContents: boolean;
  chapters: Chapter[];
  unknownChapter: { title: string; annotationCount: number };
};

/** The heading level word the page shows, mirroring `describeHeadingLevel` in the app. */
function levelWord(level: number): string {
  if (level <= 1) return "Chapter";
  return level === 2 ? "Section" : "Subsection";
}

/**
 * How many list rows the page will draw for these annotations: a passage of two or more fragments
 * is one row, and a fragment whose passage is not in this scope is the annotation it is.
 */
function rowCount(items: Annotation[]): number {
  const passages = new Set<number>();
  let rows = 0;

  for (const item of items) {
    if (item.stitchGroupId === null) {
      rows++;
      continue;
    }

    if (passages.has(item.stitchGroupId)) continue;
    passages.add(item.stitchGroupId);
    rows++;
  }

  return rows;
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_URL}${path}`);
  if (!response.ok) {
    throw new Error(`GET ${path} failed: HTTP ${response.status}`);
  }

  return (await response.json()) as T;
}

async function annotations(
  bookId: number,
  filter = "",
): Promise<Annotation[]> {
  const response = await getJson<{ items: Annotation[] }>(
    `/highlights?page=1&pageSize=200&bookId=${bookId}${filter}`,
  );

  return response.items;
}

let bookId = 0;

test.beforeAll(async () => {
  const body = new FormData();
  body.append(
    "file",
    new Blob([await readFile(CLIPPINGS)]),
    "annotation-vocabulary.txt",
  );

  const imported = await fetch(`${API_URL}/imports`, {
    method: "POST",
    body,
  });
  if (!imported.ok) {
    throw new Error(
      `Importing the annotation vocabulary failed: HTTP ${imported.status} ${await imported.text()}`,
    );
  }

  const library = await getJson<{ items: { id: number; title: string }[] }>(
    "/books?page=1&pageSize=200",
  );
  const book = library.items.find((item) => item.title === BOOK_TITLE);
  if (!book) throw new Error(`“${BOOK_TITLE}” is not in the library`);

  bookId = book.id;
});

test.beforeEach(async ({ page }) => {
  await page.goto(`/app/books/${bookId}`, { waitUntil: "networkidle" });
});

test("a heading reads as a heading, and only as the level the payload reports", async ({
  page,
}) => {
  const payload = await annotations(bookId);
  const headings = payload.filter((item) => item.headingLevel !== null);
  expect(headings.length).toBeGreaterThan(0);

  for (const heading of headings) {
    const card = page
      .locator(".hl")
      .filter({ has: page.locator(".hl-quote--heading") })
      .filter({ hasText: heading.text });

    await expect(card).toHaveCount(1);
    await expect(
      card.locator(".hl-heading"),
    ).toHaveAttribute("data-level", String(heading.headingLevel));
    await expect(card.locator(".hl-eyebrow")).toHaveText(
      levelWord(heading.headingLevel!),
    );
    await expect(card.locator(".hl-quote--heading")).toHaveText(heading.text);

    // The heading is its own chapter, so a path under it would only repeat it.
    await expect(card.locator(".hl-crumb")).toHaveCount(0);
  }

  // Everything else is a passage, never promoted to a heading.
  for (const item of payload.filter((entry) => entry.headingLevel === null)) {
    const card = page.locator(".hl").filter({ hasText: item.text });

    await expect(card.locator(".hl-quote--heading")).toHaveCount(0);
    await expect(card.locator(".hl-heading")).toHaveCount(0);
  }
});

test("a note and its tags read the way the payload reports them", async ({
  page,
}) => {
  const payload = await annotations(bookId);
  const annotated = payload.find(
    (item) => item.note?.includes("whole design principle") ?? false,
  );
  if (!annotated) throw new Error("The fixture lost its annotated highlight");

  const card = page.locator(".hl").filter({ hasText: annotated.text });

  await expect(card.locator(".hl-quote")).toHaveText(annotated.text);
  await expect(card.locator(".hl-note")).toContainText(annotated.note!);
  // The markers are vocabulary, not commentary: none of them is left in the note.
  await expect(card.locator(".hl-note")).not.toContainText(".prob");
  await expect(card.locator(".hl-note")).not.toContainText(".review");
  await expect(card.locator(".hl-tag")).toHaveText(annotated.tags);
  await expect(card.locator(".hl-crumb")).toContainText(
    annotated.chapterPath.join(" › "),
  );
});

test("a stitched passage reads as one item that keeps every fragment", async ({
  page,
}) => {
  const payload = await annotations(bookId);
  const fragments = payload.filter((item) => item.stitchGroupId !== null);
  const lead = fragments.find((item) => item.stitchIndex === 0);
  if (!lead) throw new Error("The fixture lost its stitched passage");

  const passage = page.locator(".hl--stitch");
  await expect(passage).toHaveCount(1);
  await expect(passage.locator(".hl-stitch-count")).toHaveText(
    `${fragments.length} fragments stitched`,
  );
  await expect(passage.locator(".hl-quote--passage")).toContainText(lead.text);

  // The passage carries the union of its fragments' tags, once each.
  const tags = [...new Set(fragments.flatMap((item) => item.tags))];
  await expect(passage.locator(".hl-tag")).toHaveText(tags);

  await passage.locator(".hl-summary").click();
  const cards = passage.locator(".hl-fragment");
  await expect(cards).toHaveCount(fragments.length);

  for (const [index, fragment] of fragments.entries()) {
    const card = cards.nth(index);
    await expect(card.locator(".hl-quote")).toHaveText(fragment.text);
    await expect(card.locator(".hl-provenance dd").first()).toHaveText(
      fragment.position ?? "Unknown",
    );

    if (fragment.note) {
      await expect(card.locator(".hl-note")).toContainText(fragment.note);
    } else {
      await expect(card.locator(".hl-note")).toHaveCount(0);
    }
  }
});

test("the contents list and its filter agree with the chapter payload", async ({
  page,
}) => {
  const toc = await getJson<ChapterList>(`/books/${bookId}/chapters`);
  expect(toc.hasTableOfContents).toBe(true);

  for (const chapter of toc.chapters) {
    const entry = page
      .locator(".toc-entry")
      .filter({ hasText: chapter.title })
      .first();
    await expect(entry.locator(".toc-count")).toHaveText(
      String(chapter.annotationCount),
    );
    await expect(entry.locator("..")).toHaveAttribute(
      "data-level",
      String(Math.min(chapter.level, 4)),
    );
  }

  const section = toc.chapters.find((chapter) => chapter.level === 2);
  if (!section) throw new Error("The fixture lost its section");

  const scoped = await annotations(bookId, `&chapterId=${section.id}`);
  expect(scoped.length).toBeGreaterThan(0);

  await page
    .locator(".toc-entry")
    .filter({ hasText: section.title })
    .first()
    .click();

  await expect(
    page.locator(".toc-entry", { hasText: section.title }).first(),
  ).toHaveAttribute("aria-current", "true");
  await expect(page.locator(".hl")).toHaveCount(rowCount(scoped));

  // Every row in the scope belongs to the chapter the payload says it does.
  for (const item of scoped) {
    await expect(
      page.locator(".hl").filter({ hasText: item.text }).first(),
    ).toBeVisible();
  }
});

test("the tag chips agree with the tag payload and filter to the same annotations", async ({
  page,
}) => {
  const tags = await getJson<{ tags: { name: string; count: number }[] }>(
    "/tags",
  );
  const prob = tags.tags.find((tag) => tag.name.toLowerCase() === "prob");
  if (!prob) throw new Error("The fixture lost its tag");

  const chip = page
    .locator(".tag-filter-chip")
    .filter({ hasText: prob.name })
    .first();
  await expect(chip.locator(".tag-filter-count")).toHaveText(
    String(prob.count),
  );

  const scoped = await annotations(bookId, `&tag=${prob.name}`);
  expect(scoped.length).toBeGreaterThan(0);

  await chip.click();

  await expect(chip).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator(".tag-filter-chip[data-active=true]")).toHaveCount(1);
  await expect(page.locator(".hl")).toHaveCount(rowCount(scoped));

  // Nothing survives the filter that the payload does not tag, and no dot-marker is left behind
  // as if it were part of the reader's words.
  const shown = await page.locator(".hl").allInnerTexts();
  for (const text of shown) {
    expect(text).toContain(prob.name);
    expect(text).not.toContain(`.${prob.name.toLowerCase()}`);
  }
});
