import { createFileRoute, Link } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { AlertTriangle, DatabaseZap, HardDrive, Settings } from 'lucide-react'
import { cacheApi } from '../api/cache'
import { configApi, configQk, layerPath, type ConfigKeyRow, type ConfigLayer } from '../api/config'
import type { SettingsTab } from '../api/conventions'
import { mountsApi, mountsQk } from '../api/mounts'
import { ConventionsTab } from '../components/ConventionsTab'
import { MountRowView, MountsHint } from '../components/MountRows'

const searchSchema = z.object({
  tab: z.enum(['config', 'conventions']).optional(),
})

export const Route = createFileRoute('/settings')({
  validateSearch: searchSchema,
  component: SettingsScreen,
})

const TABS: { tab: SettingsTab; label: string }[] = [
  { tab: 'config', label: 'Config' },
  { tab: 'conventions', label: 'Conventions' },
]

/**
 * Repo settings, read-only, and the conventions tab. Every config key shows —
 * this is the same table `knit config list` prints, off the same endpoint, so
 * there is no second definition of "effective value" or "source".
 *
 * Read-only is a decision, not a placeholder: mounts are filesystem paths
 * where a typo reads as "mount missing", and the rest is repo policy that
 * belongs in a committed file and a review. A form that writes committed
 * config from a browser on loopback is a bigger design than this.
 */
function SettingsScreen() {
  const tab: SettingsTab = Route.useSearch().tab ?? 'config'

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="flex items-center gap-2 px-4 py-2 border-b border-zinc-200 dark:border-zinc-800">
        <Settings className="h-4 w-4 text-zinc-500" aria-hidden />
        <h1 className="text-sm font-medium">Settings</h1>
        <nav className="ml-3 flex gap-1 text-xs" aria-label="Settings tabs">
          {TABS.map((t) => (
            <Link
              key={t.tab}
              to="/settings"
              search={t.tab === 'config' ? {} : { tab: t.tab }}
              aria-current={tab === t.tab ? 'page' : undefined}
              className={
                'rounded px-2 py-0.5 ' +
                (tab === t.tab
                  ? 'bg-zinc-200 text-zinc-900 dark:bg-zinc-800 dark:text-zinc-100'
                  : 'text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-900')
              }
            >
              {t.label}
            </Link>
          ))}
        </nav>
        {tab === 'config' && (
          <span className="ml-auto text-xs text-zinc-500">global · config.json · config.local.json</span>
        )}
      </div>

      <div className="flex-1 min-h-0 overflow-auto p-4">
        {tab === 'config' ? <ConfigTab /> : <ConventionsTab />}
      </div>
    </div>
  )
}

function ConfigTab() {
  const cfg = useQuery({ queryKey: configQk.get(), queryFn: configApi.get })
  const mounts = useQuery({ queryKey: mountsQk.list(), queryFn: mountsApi.list, staleTime: Infinity })
  const mountRows = mounts.data?.mounts ?? []

  return (
    <div className="space-y-6">
      {cfg.isLoading && <div className="text-sm text-zinc-500">Reading config…</div>}
      {cfg.error && (
        <div className="text-sm text-red-600 dark:text-red-400">
          Could not read config: {String(cfg.error)}
        </div>
      )}

      {cfg.data && cfg.data.unreadable.length > 0 && (
        <div className="flex items-start gap-2 rounded-md border border-amber-300 dark:border-amber-900 bg-amber-50 dark:bg-amber-950/40 px-3 py-2 text-xs text-amber-800 dark:text-amber-200">
          <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden />
          <div>
            {cfg.data.unreadable.join(' and ')} does not parse as JSON, so every value below is
            a fallback rather than what the file says. Fix the file — <code>knit verify</code>{' '}
            lists it too.
          </div>
        </div>
      )}

      {cfg.data && (
        <section className="space-y-1">
          <h2 className="text-xs font-medium text-zinc-500 uppercase tracking-wide">Keys</h2>
          <table className="w-full text-xs">
            <thead className="text-zinc-500">
              <tr className="text-left">
                <th className="py-1 pr-3 font-medium">Key</th>
                <th className="py-1 pr-3 font-medium">Value</th>
                <th className="py-1 pr-3 font-medium">Source</th>
                <th className="py-1 font-medium">Default</th>
              </tr>
            </thead>
            <tbody>
              {cfg.data.keys.map((row) => (
                <KeyRow key={`${row.source}:${row.key}`} row={row} layers={cfg.data.layers} />
              ))}
            </tbody>
          </table>
          {cfg.data.ignoredGlobal.length > 0 && (
            <div className="rounded-md border border-amber-300 dark:border-amber-900 bg-amber-50 dark:bg-amber-950/40 px-3 py-2 text-xs text-amber-800 dark:text-amber-200 space-y-0.5">
              <div className="font-medium">Ignored in your global config</div>
              {cfg.data.ignoredGlobal.map((i) => (
                <div key={i.key}>
                  <code>{i.key}</code> — {i.reason}
                </div>
              ))}
            </div>
          )}
          <div className="text-[11px] text-zinc-500 pt-1">
            Read-only here — change a setting with <code>knit config set &lt;key&gt; &lt;value&gt;</code>,{' '}
            <code>--local</code> for this machine only, or <code>--global</code> for every repo on
            this machine. Hover a source to see which file it is.
          </div>
        </section>
      )}

      <section className="space-y-1">
        <h2 className="flex items-center gap-1.5 text-xs font-medium text-zinc-500 uppercase tracking-wide">
          <HardDrive className="h-3.5 w-3.5" aria-hidden />
          Mounts
        </h2>
        {mountRows.length === 0 ? (
          <div className="text-xs text-zinc-500">No mounts declared.</div>
        ) : (
          mountRows.map((m) => <MountRowView key={m.name} row={m} />)
        )}
        <MountsHint />
      </section>

      <CacheSection />
    </div>
  )
}

function CacheSection() {
  const client = useQueryClient()
  const clear = useMutation({
    mutationFn: cacheApi.clear,
    onSuccess: () => client.invalidateQueries(),
  })

  return (
    <section className="space-y-1">
      <h2 className="flex items-center gap-1.5 text-xs font-medium text-zinc-500 uppercase tracking-wide">
        <DatabaseZap className="h-3.5 w-3.5" aria-hidden />
        Cache
      </h2>
      <div className="flex items-center gap-2 text-xs">
        <button
          type="button"
          onClick={() => clear.mutate()}
          disabled={clear.isPending}
          className="rounded border border-zinc-300 dark:border-zinc-700 px-2 py-0.5 hover:bg-zinc-100 dark:hover:bg-zinc-900 disabled:opacity-50"
        >
          Clear cache
        </button>
        {clear.isSuccess && <span className="text-zinc-500">Cleared.</span>}
        {clear.error && (
          <span className="text-red-600 dark:text-red-400">Could not clear: {String(clear.error)}</span>
        )}
      </div>
      <div className="text-[11px] text-zinc-500 pt-1">
        The server keeps the parsed items and checks them against the files on every request. Clear it if
        what you see here disagrees with the files.
      </div>
    </section>
  )
}

function KeyRow({ row, layers }: { row: ConfigKeyRow; layers: ConfigLayer[] }) {
  const path = layerPath(layers, row.source)
  return (
    <tr className="border-t border-zinc-100 dark:border-zinc-900 align-top">
      <td className="py-1 pr-3">
        <span className="font-mono text-zinc-800 dark:text-zinc-100">{row.key}</span>
        {!row.known && (
          <span className="ml-1.5 text-amber-600 dark:text-amber-500">(unknown key)</span>
        )}
        <div className="text-zinc-500">{row.description}</div>
      </td>
      <td className="py-1 pr-3 font-mono text-zinc-700 dark:text-zinc-200 break-all">
        {row.value || '—'}
      </td>
      <td className="py-1 pr-3">
        <span
          title={path ?? undefined}
          aria-label={path ? `${row.source} — ${path}` : undefined}
          className={
            row.isSet
              ? 'inline-block rounded px-1.5 py-0.5 bg-zinc-200 dark:bg-zinc-800 text-zinc-700 dark:text-zinc-200'
              : 'inline-block rounded px-1.5 py-0.5 text-zinc-500'
          }
        >
          {row.source}
        </span>
      </td>
      <td className="py-1 font-mono text-zinc-500 break-all">{row.default || '—'}</td>
    </tr>
  )
}
