import { ApiError } from './client'

/**
 * One repo setting from GET /api/config — the same row `knit config list`
 * prints, from the same report, so "effective value" and "source" are defined
 * once.
 */
export interface ConfigKeyRow {
  key: string
  value: string
  /** `global`, `config.json`, `config.local.json`, or `default`. */
  source: string
  default: string
  isSet: boolean
  /** False for a key knit does not model — listed, never dropped. */
  known: boolean
  settable: boolean
  description: string
}

/** A key the user-level config declares that applies to no repo. */
export interface IgnoredGlobalKey {
  key: string
  reason: string
}

/** One config layer with the file it lives in — what the source badge reveals on hover. */
export interface ConfigLayer {
  label: string
  path: string
}

export interface ConfigPayload {
  keys: ConfigKeyRow[]
  layers: ConfigLayer[]
  ignoredGlobal: IgnoredGlobalKey[]
  /** Config files that exist but do not parse; non-empty means nothing below is fact. */
  unreadable: string[]
}

export const configApi = {
  async get(): Promise<ConfigPayload> {
    const r = await fetch('/api/config')
    if (!r.ok) {
      let problem = null
      try { problem = await r.json() } catch { /* noop */ }
      throw new ApiError(r.status, problem)
    }
    return (await r.json()) as ConfigPayload
  },
}

export const configQk = {
  get: () => ['config'] as const,
}

/**
 * The file behind a row's `source`, or null for `default` (no file) and for a
 * layer the server didn't name. Shown on the badge rather than in the row: a
 * path per row is clutter in a table of short values, and "where do I change
 * it?" is the question that only comes up for one row at a time.
 */
export function layerPath(layers: ConfigLayer[], source: string): string | null {
  return layers.find((l) => l.label === source)?.path ?? null
}
