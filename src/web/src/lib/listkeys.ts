import { useEffect, useRef } from "react";
import { isTypingTarget } from "./hotkeys";

export interface ListKeyOptions {
  /** Number of rows currently rendered. */
  count: number;
  /** The row the cursor sits on. */
  cursor: number;
  /** Container the rows live in, used to tell "focus is in the list" from "focus is elsewhere". */
  containerRef: React.RefObject<HTMLElement | null>;
  /** Moves the cursor to `index` and gives that row DOM focus. */
  focusAt: (index: number) => void;
  /** Single-key actions, called with the row the cursor is on. */
  actions?: Record<string, (index: number) => void>;
}

/**
 * Installs list shortcuts at the **page** level.
 *
 * The obvious implementation — `onKeyDown` on each row — only fires once a row already
 * has DOM focus, so a user who lands on the page and presses `j` gets nothing. That is a
 * shortcut layer that works exactly when you no longer need it. Listening on the window
 * instead means the keys work the moment the list is on screen, and the first `j` or `k`
 * adopts the list rather than stepping past its first row.
 *
 * `Enter`, `Space` and `Escape` are deliberately *not* handled here: they have meaning on
 * whatever is focused, and stealing them would break every other control on the page.
 */
export function useListKeys(options: ListKeyOptions): void {
  const ref = useRef(options);
  ref.current = options;

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      const { count, cursor, containerRef, focusAt, actions } = ref.current;

      if (count === 0) return;
      if (event.ctrlKey || event.metaKey || event.altKey) return;
      if (isTypingTarget(event.target)) return;

      // A modal owns the keyboard while it is open.
      if (document.querySelector("dialog[open]")) return;

      const active = document.activeElement;
      const inList = active instanceof Node && containerRef.current?.contains(active) === true;

      const step = event.key === "j" || event.key === "ArrowDown" ? 1
        : event.key === "k" || event.key === "ArrowUp" ? -1
        : 0;

      if (step !== 0) {
        event.preventDefault();
        // Arriving from outside, the first press adopts the cursor rather than moving it,
        // so `j` never skips the row the user is looking at.
        focusAt(inList ? cursor + step : cursor);
        return;
      }

      const action = actions?.[event.key];
      if (!action) return;

      event.preventDefault();
      action(cursor);
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);
}
