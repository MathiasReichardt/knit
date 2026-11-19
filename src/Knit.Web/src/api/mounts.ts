import { ApiError } from './client'

/**
 * A config mount: a named read-only directory outside the repo root
 * that KB file sources may point into.
 *
 * Read-only in the SPA on purpose — editing one writes either `config.json`
 * (committed, shared) or `config.local.json` (this machine only), and a form
 * that silently picks one is a footgun. The doctor view shows the state and
 * the `knit mount` command to run.
 */
export interface MountRow {
  name: string
  declaredPath: string | null
  effectivePath: string
  /** Config file the value came from; null when nothing declares it. */
  source: string | null
  resolves: boolean
  /** False for an absolute declared path — it works here, not elsewhere. */
  portable: boolean
  /** KB sources riding on this mount. */
  sourceCount: number
  /** Null when the mount is fine. */
  reason: string | null
}

export const mountsApi = {
  async list(): Promise<{ mounts: MountRow[] }> {
    const r = await fetch('/api/mounts')
    if (!r.ok) {
      let problem = null
      try { problem = await r.json() } catch { /* noop */ }
      throw new ApiError(r.status, problem)
    }
    return (await r.json()) as { mounts: MountRow[] }
  },
}

export const mountsQk = {
  list: () => ['mounts'] as const,
}

/**
 * Why a source's file can't be opened, or null when it can. An undeclared or
 * unresolved mount disables only the open action — the KB entry itself stays
 * valid, so this is worded as a config problem, not a broken entry.
 */
export function mountBlockReason(mounts: MountRow[] | undefined, mount: string | null): string | null {
  if (!mount) return null
  // Until the list arrives, assume the mount is fine: flashing "unresolved"
  // on every load would be the wrong default.
  if (!mounts) return null
  const row = mounts.find((m) => m.name === mount)
  if (!row) return `mount '${mount}' is not declared`
  if (!row.resolves) return `mount '${mount}' unresolved`
  return null
}

/** Mounts needing attention — what `knit verify` reports and the doctor counts. */
export function brokenMounts(mounts: MountRow[] | undefined): MountRow[] {
  return (mounts ?? []).filter((m) => m.reason !== null)
}

/** The command that repairs a mount, for the doctor view to show and copy. */
export function mountFixCommand(row: MountRow): string {
  return row.source === 'config.local.json'
    ? `knit mount set ${row.name} <path> --local`
    : `knit mount set ${row.name} <path>`
}
