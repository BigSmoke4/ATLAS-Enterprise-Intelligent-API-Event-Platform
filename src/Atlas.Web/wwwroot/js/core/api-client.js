/**
 * ATLAS JSON client.
 *
 * One place that knows about: antiforgery headers, same-origin credentials,
 * request timeouts, and RFC7807 ProblemDetails. Errors are surfaced with the
 * server's own title/detail so the UI reports the real reason a call failed
 * (403 role, 422 domain refusal, 503 dependency outage) instead of a generic
 * "something went wrong".
 *
 * No retries are performed here: a write must never be silently repeated, and
 * reads are refreshed by the command bar on a cadence the operator controls.
 */

const DEFAULT_TIMEOUT_MS = 15000;

export class ApiError extends Error {
  constructor(message, { status = 0, detail = null, code = null, problem = null } = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.detail = detail ?? problem?.detail ?? null;
    this.code = code ?? problem?.code ?? problem?.errorCode ?? null;
    this.problem = problem;
  }
}

function csrfToken() {
  const meta = document.querySelector('meta[name="csrf-token"]');
  if (meta?.content) return meta.content;
  const input = document.querySelector('input[name="__RequestVerificationToken"]');
  return input?.value ?? null;
}

function parsePayload(text) {
  if (!text) return null;
  const contentType = 'application/json';
  try {
    return JSON.parse(text);
  } catch {
    return contentType ? { raw: text } : null;
  }
}

export async function request(url, { method = 'GET', body = null, timeoutMs = DEFAULT_TIMEOUT_MS, headers = {} } = {}) {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), timeoutMs);

  try {
    const init = {
      method,
      credentials: 'same-origin',
      headers: { Accept: 'application/json', ...headers },
      signal: controller.signal
    };

    if (body !== null && body !== undefined) {
      init.headers['Content-Type'] = 'application/json';
      init.body = typeof body === 'string' ? body : JSON.stringify(body);
    }

    const token = csrfToken();
    if (token && method !== 'GET' && method !== 'HEAD') {
      init.headers['X-CSRF-TOKEN'] = token;
    }

    const response = await fetch(url, init);
    const text = await response.text();
    const payload = parsePayload(text);

    if (!response.ok) {
      const problem = payload && typeof payload === 'object' && !Array.isArray(payload) ? payload : null;
      throw new ApiError(problem?.title ?? `${method} ${url} failed with ${response.status}`, {
        status: response.status,
        problem
      });
    }

    return payload;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    if (error?.name === 'AbortError') {
      throw new ApiError(`Request to ${url} timed out after ${Math.round(timeoutMs / 1000)}s.`, { status: 0 });
    }
    throw new ApiError(error?.message ?? 'Network error', { status: 0 });
  } finally {
    window.clearTimeout(timer);
  }
}

export const get = (url, options = {}) => request(url, { ...options, method: 'GET' });
export const post = (url, body, options = {}) => request(url, { ...options, method: 'POST', body });
export const put = (url, body, options = {}) => request(url, { ...options, method: 'PUT', body });
export const del = (url, options = {}) => request(url, { ...options, method: 'DELETE' });

/** Reads a JSON island rendered by Razor (`<script type="application/json" id="...">`). */
export function readJsonIsland(id, fallback = null) {
  const element = document.getElementById(id);
  if (!element) return fallback;
  try {
    const parsed = JSON.parse(element.textContent ?? 'null');
    return parsed ?? fallback;
  } catch {
    return fallback;
  }
}

/** Query-string helper that omits empty values instead of sending blanks. */
export function query(params) {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== null && value !== undefined && value !== '') search.set(key, String(value));
  });
  const text = search.toString();
  return text ? `?${text}` : '';
}
