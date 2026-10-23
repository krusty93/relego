/**
 * "Open in a window": the popup as a standalone window.
 *
 * A toolbar popup closes the moment the user clicks anywhere outside it, which is awkward while
 * carrying a one-time code from Relego into the form. The header control reopens this document in a
 * small window of its own, the way password managers offer it, so the fields survive the trip.
 *
 * The window carries a marker in its URL so the detached document does not offer to detach itself
 * again.
 */

/** Query parameter that marks a document as the detached window. */
export const WINDOW_PARAM = "window";

/** Detached window width, wide enough for the 360px column plus window chrome. */
export const WINDOW_WIDTH = 380;

/** Detached window height, tall enough for the whole form without scrolling. */
export const WINDOW_HEIGHT = 760;

/**
 * Reports whether the document is already the detached window.
 *
 * @param search The document's query string, marker included.
 * @returns `true` when the document was opened as the detached window.
 */
export function isWindowed(search: string): boolean {
  const marker = new URLSearchParams(search).get(WINDOW_PARAM);

  return marker !== null && marker.length > 0;
}

/**
 * Builds the URL of the detached window.
 *
 * @param popupUrl The popup document's own URL.
 * @returns The same document, marked as the detached window.
 */
export function windowedUrl(popupUrl: string): string {
  const url = new URL(popupUrl);
  url.searchParams.set(WINDOW_PARAM, "1");

  return url.toString();
}
