import { mountFixCommand, type MountRow } from '../api/mounts'
import { CopyCommandIconButton } from './CopyButton'

/**
 * One mount row, read-only. Shared by the doctor view and the settings
 * view so the `mounts` key has a single rendering wherever it is shown —
 * `knit config list` renders it through the same one on the CLI side.
 */
export function MountRowView({ row }: { row: MountRow }) {
  const sources = `${row.sourceCount} source${row.sourceCount === 1 ? '' : 's'}`
  return (
    <div className="flex items-start gap-2 py-0.5 text-xs">
      <span className="font-mono font-medium text-zinc-700 dark:text-zinc-200 shrink-0">{row.name}</span>
      <div className="min-w-0 flex-1">
        <div className="font-mono text-zinc-600 dark:text-zinc-300 break-all">
          {row.declaredPath ?? '(undeclared)'}
          {row.declaredPath && row.effectivePath !== row.declaredPath && (
            <span className="text-zinc-400 dark:text-zinc-500"> → {row.effectivePath}</span>
          )}
        </div>
        <div className="text-zinc-500">
          {row.source ?? 'not declared in any config'} · {sources}
          {row.reason && (
            <span className="text-amber-600 dark:text-amber-500"> · {row.reason}</span>
          )}
        </div>
      </div>
      {row.reason && (
        <div className="flex items-center gap-1 shrink-0">
          <code className="text-[11px] text-zinc-500">{mountFixCommand(row)}</code>
          <CopyCommandIconButton value={mountFixCommand(row)} />
        </div>
      )}
    </div>
  )
}

/** Why the SPA shows mounts but never writes them. */
export function MountsHint() {
  return (
    <div className="text-[11px] text-zinc-500 pt-1">
      Mounts are read-only here — declare or repoint one with <code>knit mount</code>. A committed
      path is shared with everyone; <code>--local</code> overrides it on this machine only.
    </div>
  )
}
