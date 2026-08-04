import { API_URL } from "./config";
import type {
  BooksResponse,
  ExclusionsResponse,
  HighlightsResponse,
  ImportResponse,
  RecapHistoryResponse,
  SettingsResponse,
  SmtpSettingsResponse,
  SmtpTestResponse,
  StatusResponse,
  UpdateSettingsRequest,
  UpdateSmtpSettingsRequest,
  WeightedHighlight,
} from "./types";

/**
 * An error the UI can show verbatim. `fieldErrors` carries RFC 9457 validation
 * problems so a form can put the message next to the field that caused it.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors: Record<string, string[]>;

  constructor(message: string, status: number, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.fieldErrors = fieldErrors;
  }

  /** The first message for a field, if the server reported one. */
  field(name: string): string | undefined {
    const key = Object.keys(this.fieldErrors).find(
      (k) => k.toLowerCase() === name.toLowerCase(),
    );
    return key ? this.fieldErrors[key]?.[0] : undefined;
  }
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

async function toError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {};

  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // Not a problem+json body; fall through to the generic message.
  }

  const fieldErrors = problem.errors ?? {};
  const firstFieldMessage = Object.values(fieldErrors)[0]?.[0];

  const message =
    problem.detail ??
    firstFieldMessage ??
    problem.title ??
    `The server responded with ${response.status}.`;

  return new ApiError(message, response.status, fieldErrors);
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${API_URL}${path}`, {
      ...init,
      headers: {
        Accept: "application/json",
        ...(init?.body instanceof FormData ? {} : { "Content-Type": "application/json" }),
        ...init?.headers,
      },
    });
  } catch {
    throw new ApiError("Could not reach this Relego server. Check that it is running.", 0);
  }

  if (!response.ok) {
    throw await toError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  status: () => request<StatusResponse>("/status"),

  books: (query?: string) =>
    request<BooksResponse>(
      `/books?pageSize=500${query ? `&q=${encodeURIComponent(query)}` : ""}`,
    ),

  renameBook: (id: number, title: string) =>
    request<void>(`/books/${id}/title`, { method: "PUT", body: JSON.stringify({ title }) }),

  highlights: (query?: string, page = 1, pageSize = 200) =>
    request<HighlightsResponse>(
      `/highlights?page=${page}&pageSize=${pageSize}${query ? `&q=${encodeURIComponent(query)}` : ""}`,
    ),

  deleteHighlight: (id: number) => request<void>(`/highlights/${id}`, { method: "DELETE" }),

  weights: () => request<WeightedHighlight[]>("/highlights/weights"),

  setWeight: (id: number, weight: number) =>
    request<void>(`/highlights/${id}/weight`, { method: "PUT", body: JSON.stringify({ weight }) }),

  exclusions: () => request<ExclusionsResponse>("/exclusions"),

  exclude: (kind: "highlights" | "books" | "authors", id: number) =>
    request<void>(`/${kind}/${id}/exclusions`, { method: "POST" }),

  include: (kind: "highlights" | "books" | "authors", id: number) =>
    request<void>(`/${kind}/${id}/exclusions`, { method: "DELETE" }),

  settings: () => request<SettingsResponse>("/settings"),

  updateSettings: (body: UpdateSettingsRequest) =>
    request<SettingsResponse>("/settings", { method: "PATCH", body: JSON.stringify(body) }),

  testKindleEmail: () => request<unknown>("/settings/test-kindle-email", { method: "POST" }),

  testRecapEmail: () => request<unknown>("/settings/test-recap-email", { method: "POST" }),

  smtp: () => request<SmtpSettingsResponse>("/settings/smtp"),

  updateSmtp: (body: UpdateSmtpSettingsRequest) =>
    request<SmtpSettingsResponse>("/settings/smtp", { method: "PUT", body: JSON.stringify(body) }),

  testSmtp: (toAddress?: string) =>
    request<SmtpTestResponse>("/settings/smtp/test", {
      method: "POST",
      body: JSON.stringify({ toAddress: toAddress || null }),
    }),

  recapHistory: () => request<RecapHistoryResponse>("/recaps?limit=20"),

  sendRecapNow: () => request<unknown>("/recaps", { method: "POST" }),

  /** Uploads an export file. `onProgress` reports 0–1 so the UI can show real progress. */
  importFile: (file: File, onProgress?: (fraction: number) => void) =>
    new Promise<ImportResponse>((resolve, reject) => {
      const form = new FormData();
      form.append("file", file);

      const xhr = new XMLHttpRequest();
      xhr.open("POST", `${API_URL}/imports`);
      xhr.responseType = "json";

      xhr.upload.addEventListener("progress", (event) => {
        if (event.lengthComputable) onProgress?.(event.loaded / event.total);
      });

      xhr.addEventListener("load", () => {
        const body = xhr.response as (ProblemDetails & ImportResponse) | null;

        if (xhr.status >= 200 && xhr.status < 300) {
          onProgress?.(1);
          resolve(body as ImportResponse);
          return;
        }

        const fieldErrors = body?.errors ?? {};
        reject(
          new ApiError(
            body?.detail ??
              Object.values(fieldErrors)[0]?.[0] ??
              body?.title ??
              `The server responded with ${xhr.status}.`,
            xhr.status,
            fieldErrors,
          ),
        );
      });

      xhr.addEventListener("error", () =>
        reject(new ApiError("Could not reach this Relego server.", 0)),
      );

      xhr.addEventListener("abort", () => reject(new ApiError("Upload cancelled.", 0)));

      xhr.send(form);
    }),
};
