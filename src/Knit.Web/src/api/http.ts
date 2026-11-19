// The one place the SPA sends a write. The API refuses a write without
// `Content-Type: application/json` — also a bodyless one — because a page on
// another site can't send that type without a CORS preflight the API never
// grants. ESLint forbids `fetch` with a `method` anywhere else.

export interface ProblemDetails {
  title: string
  status: number
  detail?: string | null
  code?: string | null
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null
  readonly etag: string | null

  constructor(status: number, problem: ProblemDetails | null, etag?: string | null) {
    super(problem?.detail || problem?.title || `HTTP ${status}`)
    this.status = status
    this.problem = problem
    this.etag = etag ?? null
  }
}

export async function parseProblem(r: Response): Promise<ProblemDetails | null> {
  try {
    const text = await r.text()
    if (!text) return null
    return JSON.parse(text) as ProblemDetails
  } catch { return null }
}

export type WriteMethod = 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export function apiWrite(
  url: string,
  method: WriteMethod,
  opts: { body?: unknown; ifMatch?: string } = {},
): Promise<Response> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (opts.ifMatch !== undefined) headers['If-Match'] = opts.ifMatch
  return fetch(url, {
    method,
    headers,
    body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
  })
}

async function readJson<T>(r: Response): Promise<T> {
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r))
  return (await r.json()) as T
}

/** GET, throwing `ApiError` with the server's problem details. */
export async function getJson<T>(url: string): Promise<T> {
  return readJson<T>(await fetch(url))
}

/** Write and read the JSON reply, throwing `ApiError` on failure. */
export async function sendJson<T>(url: string, method: WriteMethod, body?: unknown): Promise<T> {
  return readJson<T>(await apiWrite(url, method, { body }))
}

export function postJson<T>(url: string, body: unknown): Promise<T> {
  return sendJson<T>(url, 'POST', body)
}
