import { useEffect, useRef } from "react";

/** True when the event came from a text field, so global single-key shortcuts must not fire. */
export function isTypingTarget(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  if (!element) return false;

  const tag = element.tagName;
  return (
    tag === "INPUT" ||
    tag === "TEXTAREA" ||
    tag === "SELECT" ||
    element.isContentEditable === true
  );
}

export interface HotkeyHandlers {
  /** Single keys, e.g. `{ "/": fn, "?": fn }`. Ignored while typing. */
  keys?: Record<string, (event: KeyboardEvent) => void>;
  /** Two-key chords following `g`, e.g. `{ l: fn }` for `g l`. */
  goChords?: Record<string, () => void>;
  /** Ctrl/Cmd combinations, keyed by lower-case letter. */
  meta?: Record<string, (event: KeyboardEvent) => void>;
}

/**
 * Installs the global keyboard layer.
 *
 * `g` opens a 1.2s chord window so `g l` / `g s` work as go-to chords, and every
 * handler is suppressed while focus sits in a text field so typing is never intercepted.
 */
export function useHotkeys(handlers: HotkeyHandlers): void {
  const ref = useRef(handlers);
  ref.current = handlers;

  useEffect(() => {
    let chordArmed = false;
    let chordTimer = 0;

    function disarm() {
      chordArmed = false;
      window.clearTimeout(chordTimer);
    }

    function onKeyDown(event: KeyboardEvent) {
      const { keys, goChords, meta } = ref.current;

      if (event.ctrlKey || event.metaKey) {
        const handler = meta?.[event.key.toLowerCase()];
        if (handler) {
          event.preventDefault();
          handler(event);
        }
        return;
      }

      if (event.altKey) return;

      if (isTypingTarget(event.target)) return;

      if (chordArmed) {
        const chord = goChords?.[event.key.toLowerCase()];
        disarm();
        if (chord) {
          event.preventDefault();
          chord();
        }
        return;
      }

      if (event.key === "g" && goChords) {
        chordArmed = true;
        chordTimer = window.setTimeout(disarm, 1200);
        return;
      }

      const handler = keys?.[event.key];
      if (handler) {
        event.preventDefault();
        handler(event);
      }
    }

    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.clearTimeout(chordTimer);
    };
  }, []);
}
