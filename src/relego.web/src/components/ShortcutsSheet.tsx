import { useEffect, useRef, type ReactNode } from "react";

function Row({ keys, children }: { keys: ReactNode; children: ReactNode }) {
  return (
    <div>
      <dt>{keys}</dt>
      <dd>{children}</dd>
    </div>
  );
}

export function ShortcutsSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) dialog.showModal();
    else if (!open && dialog.open) dialog.close();
  }, [open]);

  return (
    <dialog
      ref={dialogRef}
      aria-labelledby="sheet-h"
      onClose={onClose}
      onClick={(event) => {
        if (event.target === dialogRef.current) onClose();
      }}
    >
      <div className="sheet">
        <h2 id="sheet-h">Keyboard shortcuts</h2>

        <h3 className="grp-title">Global</h3>
        <dl>
          <Row
            keys={
              <>
                <kbd>Ctrl</kbd> <kbd>K</kbd>
              </>
            }
          >
            Command palette
          </Row>
          <Row keys={<kbd>/</kbd>}>Focus search</Row>
          <Row
            keys={
              <>
                <kbd>g</kbd> <kbd>l</kbd>
              </>
            }
          >
            Go to library
          </Row>
          <Row
            keys={
              <>
                <kbd>g</kbd> <kbd>h</kbd>
              </>
            }
          >
            Go to highlights
          </Row>
          <Row
            keys={
              <>
                <kbd>g</kbd> <kbd>r</kbd>
              </>
            }
          >
            Go to recaps
          </Row>
          <Row
            keys={
              <>
                <kbd>g</kbd> <kbd>i</kbd>
              </>
            }
          >
            Go to import
          </Row>
          <Row
            keys={
              <>
                <kbd>g</kbd> <kbd>s</kbd>
              </>
            }
          >
            Go to settings
          </Row>
          <Row keys={<kbd>t</kbd>}>Cycle theme</Row>
          <Row keys={<kbd>?</kbd>}>This sheet</Row>
        </dl>

        <h3 className="grp-title">Lists</h3>
        <dl>
          <Row
            keys={
              <>
                <kbd>j</kbd> <kbd>k</kbd>
              </>
            }
          >
            Move down / up
          </Row>
          <Row keys={<kbd>Enter</kbd>}>Open or expand</Row>
          <Row keys={<kbd>e</kbd>}>Exclude / include</Row>
          <Row
            keys={
              <>
                <kbd>1</kbd>–<kbd>5</kbd>
              </>
            }
          >
            Set recap weight
          </Row>
          <Row keys={<kbd>Esc</kbd>}>Back or close</Row>
        </dl>

        <button className="btn" type="button" onClick={onClose}>
          Close
        </button>
      </div>
    </dialog>
  );
}
