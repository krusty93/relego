/**
 * Copies text to the clipboard.
 *
 * The async Clipboard API only exists in a secure context, and a self-hosted Relego is
 * routinely served over plain HTTP on a LAN address. Fall back to the legacy selection
 * trick there so copying still works.
 */
export async function copyToClipboard(text: string): Promise<boolean> {
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Permission denied or the document lost focus: try the legacy path instead.
    }
  }

  return legacyCopy(text);
}

function legacyCopy(text: string): boolean {
  const area = document.createElement("textarea");
  area.value = text;
  area.setAttribute("readonly", "");
  area.style.position = "fixed";
  area.style.top = "0";
  area.style.opacity = "0";
  document.body.appendChild(area);

  let copied = false;
  try {
    area.select();
    copied = document.execCommand("copy");
  } catch {
    copied = false;
  } finally {
    area.remove();
  }

  return copied;
}
