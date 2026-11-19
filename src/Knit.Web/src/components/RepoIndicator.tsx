import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Forward, GitBranch, GitFork, TriangleAlert } from 'lucide-react'
import { api, type UnsharedWorktree } from '../api/client'
import {
  headLabel, headTitle, repoApi, repoQk, unsharedCommands, unsharedSummary, worktreeTitle, type GitHead,
} from '../api/repo'
import { CopyCommandIconButton } from './CopyButton'
import { InlinePopover } from './inline/InlinePopover'

export function RepoIndicator() {
  // Same key + queryFn as the persisted-expand store's repo lookup
  // (stores/treeOpen), so the root is fetched once for both.
  const root = useQuery({ queryKey: ['root'], queryFn: api.root, staleTime: Infinity })
  // Pushed, not polled: the host watches HEAD and the SSE `git` event
  // invalidates this query.
  const git = useQuery({ queryKey: repoQk.git, queryFn: repoApi.git, staleTime: Infinity })

  const path = root.data?.rootPath ?? (root.isLoading ? '…' : '(unknown)')
  const from = root.data?.redirectedFrom
  const unshared = root.data?.unshared

  // A redirect is always visible: editing shared state while
  // believing it is this checkout's own is the mistake it invites.
  return (
    <div className="flex items-center gap-2 min-w-0 text-sm text-zinc-600 dark:text-zinc-400">
      <span className="shrink-0 text-zinc-400 dark:text-zinc-500">repo:</span>
      {from && (
        <span
          className="shrink-0 text-amber-600 dark:text-amber-400"
          title={`Redirected from ${from} — see \`knit root\``}
          aria-label={`Knit root redirected from ${from}`}
        >
          <Forward size={14} aria-hidden />
        </span>
      )}
      <span className="truncate font-mono" aria-label="Current repo root">
        {path}
      </span>
      {unshared && <UnsharedMarker unshared={unshared} />}
      {git.data && <GitHeadLabel head={git.data} />}
    </div>
  )
}

/**
 * The CLI's undecided-worktree note, for someone who only uses the SPA: the
 * data shown is this branch's copy while another checkout holds the shared one.
 * A click popover, not a tooltip — the way out is a command to copy, and a
 * native tooltip can't be selected.
 */
function UnsharedMarker({ unshared }: { unshared: UnsharedWorktree }) {
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null)
  const [open, setOpen] = useState(false)

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  return (
    <>
      <button
        ref={setAnchor}
        type="button"
        onClick={() => setOpen(o => !o)}
        aria-expanded={open}
        className="shrink-0 inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium text-amber-700 dark:text-amber-300 bg-amber-100/70 dark:bg-amber-950/50 hover:bg-amber-200 dark:hover:bg-amber-900"
      >
        <TriangleAlert size={12} aria-hidden />
        not shared
      </button>
      {open && (
        <InlinePopover anchor={anchor} onClose={() => setOpen(false)} className="w-[28rem] max-w-[calc(100vw-1rem)] p-3 space-y-2 text-xs">
          <p className="text-zinc-700 dark:text-zinc-300">{unsharedSummary(unshared)}</p>
          <ul className="space-y-1">
            {unsharedCommands(unshared).map(({ label, command }) => (
              <li key={command} className="flex items-center gap-2">
                <span className="w-24 shrink-0 text-zinc-500 dark:text-zinc-400">{label}</span>
                <code className="min-w-0 flex-1 break-all select-all rounded bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 font-mono text-zinc-800 dark:text-zinc-200">
                  {command}
                </code>
                <CopyCommandIconButton value={command} />
              </li>
            ))}
          </ul>
          <p className="text-zinc-500 dark:text-zinc-400">Run it in this worktree, then restart the web UI.</p>
        </InlinePopover>
      )}
    </>
  )
}

function GitHeadLabel({ head }: { head: GitHead }) {
  const label = headLabel(head)
  const worktree = worktreeTitle(head)
  if (!label && !worktree) return null
  return (
    <span className="shrink-0 flex items-center gap-1.5">
      <span className="text-zinc-400 dark:text-zinc-500" aria-hidden>·</span>
      {worktree && (
        <span className="text-violet-600 dark:text-violet-400" title={worktree} aria-label={worktree}>
          <GitFork size={14} aria-hidden />
        </span>
      )}
      {label && (
        <span
          className="flex items-center gap-1 font-mono text-zinc-800 dark:text-zinc-200"
          title={headTitle(head) ?? undefined}
          aria-label={`Git ${head.branch ? 'branch' : 'commit'} ${label}`}
        >
          <GitBranch size={14} aria-hidden />
          {label}
        </span>
      )}
    </span>
  )
}

export function VersionIndicator() {
  const version = useQuery({ queryKey: ['version'], queryFn: api.version, staleTime: Infinity })

  const ver = version.data?.version ?? '…'

  return (
    <span className="font-mono text-xs text-zinc-500 dark:text-zinc-500">
      v{ver}
    </span>
  )
}
