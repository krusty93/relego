import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { API_URL } from "../playwright.config";

const FIXTURES = fileURLToPath(new URL("../../Relego.Tests/Fixtures/", import.meta.url));
const SEED_FILES = ["kindle-highlights.txt", "kobo-highlights.sqlite"];

async function waitForApi(): Promise<void> {
  const deadline = Date.now() + 240_000;
  let lastError = "not started";

  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${API_URL}/status`);
      if (response.ok) return;
      lastError = `HTTP ${response.status}`;
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
    }
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }

  throw new Error(`Relego server never became ready at ${API_URL} (${lastError}).`);
}

export default async function globalSetup(): Promise<void> {
  await waitForApi();

  const books = (await (await fetch(`${API_URL}/books`)).json()) as { books?: unknown[] };
  if (books.books?.length) return;

  for (const name of SEED_FILES) {
    const body = new FormData();
    body.append("file", new Blob([await readFile(FIXTURES + name)]), name);

    const response = await fetch(`${API_URL}/imports`, { method: "POST", body });
    if (!response.ok) {
      throw new Error(`Seeding ${name} failed: HTTP ${response.status} ${await response.text()}`);
    }
  }
}
