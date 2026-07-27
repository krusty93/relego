import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRef, useState, type DragEvent } from "react";
import { useNavigate } from "react-router";
import { UploadIcon } from "../components/icons";
import { ErrorNote } from "../components/ui";
import { api, ApiError } from "../lib/api";
import { formatBytes, formatCount, plural } from "../lib/format";
import { useToasts } from "../lib/toasts";
import type { ImportResponse } from "../lib/types";

const MAX_BYTES = 64 * 1024 * 1024;

export function ImportPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { push } = useToasts();

  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [progress, setProgress] = useState(0);
  const [fileName, setFileName] = useState("");
  const [result, setResult] = useState<ImportResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const upload = useMutation({
    mutationFn: (file: File) => api.importFile(file, setProgress),
    onSuccess: (response) => {
      setResult(response);
      setError(null);
      void queryClient.invalidateQueries();
      push(
        response.newHighlights > 0
          ? `Added ${plural(response.newHighlights, "highlight")}.`
          : "Nothing new — Relego already had every highlight in that file.",
      );
    },
    onError: (uploadError) => {
      setResult(null);
      setError(
        uploadError instanceof ApiError
          ? uploadError.message
          : "The upload failed. Check that the server is running and try again.",
      );
    },
  });

  function start(file: File | undefined) {
    if (!file) return;

    if (file.size === 0) {
      setResult(null);
      setError(`${file.name} is empty. Copy the file off your reader again and retry.`);
      return;
    }

    if (file.size > MAX_BYTES) {
      setResult(null);
      setError(
        `${file.name} is ${formatBytes(file.size)}. The limit is ${formatBytes(MAX_BYTES)}.`,
      );
      return;
    }

    setFileName(file.name);
    setProgress(0);
    setError(null);
    setResult(null);
    upload.mutate(file);
  }

  function onDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setDragging(false);
    start(event.dataTransfer.files[0]);
  }

  return (
    <section className="view" aria-labelledby="im-h">
      <div className="view-head">
        <div>
          <h1 id="im-h">Import highlights</h1>
          <p>
            Plug your reader into this computer and drop its highlight file here. Nothing leaves
            your network.
          </p>
        </div>
      </div>

      {/* The dropzone is a drop target for pointer users; the button inside is the
          keyboard and screen-reader path, so the div itself needs no tabindex. */}
      <div
        className="dropzone"
        data-over={dragging || undefined}
        onDragOver={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
      >
        <UploadIcon strokeWidth={1.5} />
        <h2 className="dz-title">
          Drop <code>My Clippings.txt</code> or <code>KoboReader.sqlite</code>
        </h2>
        <p>Files stay on your network. Up to {formatBytes(MAX_BYTES)}.</p>
        {/* The input is the real control, so it keeps its own label and focus ring;
            the label around it is what people see and click. */}
        <label className="btn dz-file" data-disabled={upload.isPending || undefined}>
          <span>{upload.isPending ? "Uploading…" : "Choose a file"}</span>
          <input
            ref={inputRef}
            className="sr-only"
            type="file"
            accept=".txt,.sqlite,.db,text/plain,application/vnd.sqlite3,application/octet-stream"
            disabled={upload.isPending}
            onChange={(event) => {
              start(event.target.files?.[0]);
              event.target.value = "";
            }}
          />
        </label>
      </div>

      <dl className="tips">
        <div className="tip">
          <dt>Kindle</dt>
          <dd>
            Connect by USB, open the <code>documents</code> folder, take{" "}
            <code>My Clippings.txt</code>.
          </dd>
        </div>
        <div className="tip">
          <dt>Kobo</dt>
          <dd>
            Connect by USB, show hidden files, take <code>.kobo/KoboReader.sqlite</code>.
          </dd>
        </div>
      </dl>

      <p className="subtle">
        Relego skips highlights it already has, so importing the same file twice is safe.
      </p>

      {upload.isPending ? (
        <div className="panel import-panel">
          <header>
            <h2>Uploading {fileName}</h2>
          </header>
          <div
            className="progress"
            role="progressbar"
            aria-valuenow={Math.round(progress * 100)}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-label="Upload progress"
          >
            <i style={{ transform: `scaleX(${progress})` }} />
          </div>
          <p className="subtle hint-line">
            {progress >= 1
              ? "Uploaded. The server is reading the file…"
              : `${Math.round(progress * 100)}% sent.`}
          </p>
        </div>
      ) : null}

      {error ? (
        <div className="import-panel">
          <ErrorNote message={error} onRetry={() => inputRef.current?.click()} />
        </div>
      ) : null}

      {result ? (
        <div className="panel import-panel" aria-live="polite">
          <header>
            <h2>Imported {result.fileName}</h2>
            <p>
              Read as {result.sourceName}. {formatCount(result.entriesProcessed)} entries
              processed.
            </p>
          </header>
          <dl className="dl">
            <dt>New highlights</dt>
            <dd>{formatCount(result.newHighlights)}</dd>
            <dt>Already had</dt>
            <dd>{formatCount(result.duplicateHighlights)}</dd>
            <dt>New books</dt>
            <dd>{formatCount(result.newBooks)}</dd>
            <dt>New authors</dt>
            <dd>{formatCount(result.newAuthors)}</dd>
          </dl>
          <div className="inline dialog-actions">
            <button className="btn btn--primary" type="button" onClick={() => navigate("/")}>
              Go to library
            </button>
            <button className="btn" type="button" onClick={() => inputRef.current?.click()}>
              Import another file
            </button>
          </div>
        </div>
      ) : null}
    </section>
  );
}
