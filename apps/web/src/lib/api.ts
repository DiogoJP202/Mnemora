export type AuthUser = { id: string; email: string; isAdmin: boolean };
type RequestOptions = { signal?: AbortSignal };

export async function apiGet<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await fetch(path, { credentials: "same-origin", cache: "no-store", signal: options.signal });
  if (!response.ok) throw await responseError(response);
  return response.json() as Promise<T>;
}

export async function apiWrite<T = void>(
  path: string,
  method: "POST" | "PUT" | "PATCH" | "DELETE",
  body?: unknown,
  options: RequestOptions = {},
): Promise<T> {
  return apiWriteBody<T>(path, method, body === undefined ? undefined : JSON.stringify(body), options);
}

export class ApiResponseError extends Error {
  readonly code: string | null;
  constructor(message: string, readonly payload: unknown, readonly status = 0, readonly retryAfterSeconds: number | null = null) {
    super(message);
    this.name = "ApiResponseError";
    const problem = record(payload);
    const code = problem.code ?? record(problem.extensions).code;
    this.code = typeof code === "string" ? code : null;
  }
}

export async function apiWriteJsonDocument<T>(path: string, json: string, options: RequestOptions = {}): Promise<T> {
  return apiWriteBody<T>(path, "POST", json, options);
}

async function apiWriteBody<T>(
  path: string,
  method: "POST" | "PUT" | "PATCH" | "DELETE",
  body: string | undefined,
  options: RequestOptions,
): Promise<T> {
  const csrf = await apiGet<{ token: string }>("/api/auth/csrf", options);
  const response = await fetch(path, {
    method,
    credentials: "same-origin",
    cache: "no-store",
    headers: {
      "X-CSRF-TOKEN": csrf.token,
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
    },
    body,
    signal: options.signal,
  });
  if (!response.ok) throw await responseError(response);
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

function record(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

async function responseError(response: Response): Promise<ApiResponseError> {
    const payload: unknown = await response.json().catch(() => null);
    const problem = record(payload);
    const retry = Number(response.headers.get("Retry-After") ?? problem.retryAfterSeconds ?? record(problem.extensions).retryAfterSeconds);
    const delay = Number.isFinite(retry) && retry >= 0 ? Math.min(86400, Math.ceil(retry)) : null;
    return new ApiResponseError(errorMessage(payload), payload, response.status, delay);
}

function errorMessage(payload: unknown): string {
    if (typeof payload === "string" && payload.trim()) return payload;
    if (payload && typeof payload === "object") {
      const problem = payload as { title?: unknown; detail?: unknown };
      if (typeof problem.detail === "string" && problem.detail.trim()) return problem.detail;
      if (typeof problem.title === "string" && problem.title.trim()) return problem.title;
    }
    return "Não foi possível concluir a operação.";
}

export function downloadJson(fileName: string, json: string) {
  const url = URL.createObjectURL(new Blob([json], { type: "application/json" }));
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
