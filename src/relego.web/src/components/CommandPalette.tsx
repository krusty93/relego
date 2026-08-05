import { useEffect, useMemo, useRef, useState } from "react";
import { SearchIcon } from "./icons";

export interface Command {
  id: string;
  group: string;
  label: string;
  hint?: string;
  run: () => void;
}

/**
 * Command palette. Built on `<dialog>` so the browser handles the top layer, focus trap
 * and Esc; the list is a real listbox driven by `aria-activedescendant`, which keeps
 * keyboard focus in the input while screen readers announce the highlighted option.
 */
export function CommandPalette({
  open,
  onClose,
  commands,
}: {
  open: boolean;
  onClose: () => void;
  commands: Command[];
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);

  const matches = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return commands;
    return commands.filter((command) => command.label.toLowerCase().includes(needle));
  }, [commands, query]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      setQuery("");
      setActiveIndex(0);
      dialog.showModal();
      inputRef.current?.focus();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  useEffect(() => {
    setActiveIndex(0);
  }, [query]);

  const active = matches[Math.min(activeIndex, matches.length - 1)];

  // Group headers are rendered inline, so the rows carry their own group boundary.
  const rows = matches.map((command, index) => ({
    command,
    index,
    startsGroup: index === 0 || matches[index - 1]!.group !== command.group,
  }));

  function onKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (matches.length === 0) return;
      const delta = event.key === "ArrowDown" ? 1 : -1;
      setActiveIndex((current) => (current + delta + matches.length) % matches.length);
      return;
    }

    if (event.key === "Enter") {
      event.preventDefault();
      if (active) {
        onClose();
        active.run();
      }
    }
  }

  return (
    <dialog
      ref={dialogRef}
      aria-label="Command palette"
      onClose={onClose}
      onClick={(event) => {
        if (event.target === dialogRef.current) onClose();
      }}
    >
      <div className="palette-input">
        <SearchIcon width={18} height={18} strokeWidth={1.8} />
        <input
          ref={inputRef}
          type="text"
          role="combobox"
          aria-expanded="true"
          aria-controls="palette-list"
          {...(active ? { "aria-activedescendant": `pal-${active.id}` } : {})}
          aria-autocomplete="list"
          autoComplete="off"
          placeholder="Search commands…"
          aria-label="Command palette search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={onKeyDown}
        />
        <kbd aria-hidden="true">Esc</kbd>
      </div>

      <ul className="palette-list" id="palette-list" role="listbox" aria-label="Commands">
        {rows.flatMap(({ command, index, startsGroup }) => {
          const option = (
            <li
              key={command.id}
              id={`pal-${command.id}`}
              role="option"
              aria-selected={index === activeIndex}
              data-active={index === activeIndex ? "true" : undefined}
              onMouseMove={() => setActiveIndex(index)}
              onClick={() => {
                onClose();
                command.run();
              }}
            >
              <span>{command.label}</span>
              {command.hint ? <kbd aria-hidden="true">{command.hint}</kbd> : null}
            </li>
          );

          if (!startsGroup) return [option];

          return [
            <li className="grp" role="presentation" key={`${command.id}-group`}>
              {command.group}
            </li>,
            option,
          ];
        })}

        {matches.length === 0 ? (
          <li className="palette-empty" role="presentation">
            Nothing matches “{query.trim()}”.
          </li>
        ) : null}
      </ul>
    </dialog>
  );
}
