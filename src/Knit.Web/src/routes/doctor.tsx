import { useState } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Activity,
  AlertTriangle,
  Ban,
  CheckCircle2,
  Copy,
  FileQuestion,
  FileWarning,
  Fingerprint,
  Flag,
  FolderX,
  Link2Off,
  ListTree,
  Loader2,
  Milestone,
  Network,
  HardDrive,
  Rows3,
  RefreshCw,
  Type,
  Wrench,
} from 'lucide-react'
import {
  doctorApi,
  doctorQk,
  verifyIssueCount,
  type LinkTargetRow,
  type VerifyResult,
} from '../api/doctor'
import { CopyCommandIconButton } from '../components/CopyButton'
import { brokenMounts, mountsApi, mountsQk, type MountRow } from '../api/mounts'
import { MountRowView, MountsHint } from '../components/MountRows'
import { navTarget, resolveLink } from '../lib/resolveLink'
import { useDialogs } from '../stores/dialogs'
import { useToasts } from '../stores/toasts'

export const Route = createFileRoute('/doctor')({
  component: DoctorScreen,
})

function DoctorScreen() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const push = useToasts((s) => s.push)

  const verify = useQuery({
    queryKey: doctorQk.verify,
    queryFn: doctorApi.verify,
    retry: false,
  })
  // Busy only for a run the user asked for, not for background refetches.
  const [running, setRunning] = useState(false)
  const mounts = useQuery({ queryKey: mountsQk.list(), queryFn: mountsApi.list, staleTime: Infinity })
  const broken = brokenMounts(mounts.data?.mounts)

  const goSource = (source: string) => {
    // resolveLink with an empty parent, since the path is already
    // repo-relative. Files verify just scanned definitely exist, so we
    // navigate straight in — no checkLinkExists probe.
    const target = navTarget(resolveLink('', source))
    if (target) void navigate({ to: target.to, search: target.search as never })
    else void navigate({ to: '/file', search: { path: source } as never })
  }

  const data = verify.data
  const total = data ? verifyIssueCount(data) + broken.length : 0
  const lastChecked = verify.dataUpdatedAt ? new Date(verify.dataUpdatedAt) : null

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="px-4 py-3 border-b border-zinc-200 dark:border-zinc-800 flex items-center gap-3">
        <h1 className="text-sm font-semibold text-zinc-800 dark:text-zinc-100">Doctor</h1>
        <div className="flex-1" />
        {lastChecked && (
          <span className="text-xs text-zinc-500" title={lastChecked.toISOString()}>
            Last checked {lastChecked.toLocaleTimeString()}
          </span>
        )}
        <button
          type="button"
          onClick={() => {
            setRunning(true)
            void verify.refetch().finally(() => setRunning(false))
            // Mount config is read once and cached forever — it only changes
            // via `knit mount` in a terminal, which the SPA cannot observe.
            // This button is the way back in sight without a page reload.
            void qc.invalidateQueries({ queryKey: mountsQk.list() })
          }}
          disabled={running}
          title="Re-run the verify scan and re-read mount config"
          aria-label="Run verify"
          className="inline-flex items-center gap-1.5 h-8 rounded-md border border-zinc-300 dark:border-zinc-700 px-3 text-xs font-medium text-zinc-700 dark:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-800 disabled:opacity-50"
        >
          {running ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden />
          ) : (
            <RefreshCw className="h-3.5 w-3.5" aria-hidden />
          )}
          Run verify
        </button>
      </div>

      <div className="flex-1 min-h-0 overflow-auto p-4 space-y-6">
        <Mounts rows={broken} />
        {verify.isLoading && <div className="text-sm text-zinc-500">Running verify…</div>}
        {verify.error && (
          <div className="text-sm text-red-600 dark:text-red-400">
            Verify failed: {String(verify.error)}
          </div>
        )}
        {data && <Results data={data} total={total} goSource={goSource} push={push} qc={qc} />}
      </div>
    </div>
  )
}

function Results({
  data,
  total,
  goSource,
  push,
  qc,
}: {
  data: VerifyResult
  total: number
  goSource: (source: string) => void
  push: ReturnType<typeof useToasts.getState>['push']
  qc: ReturnType<typeof useQueryClient>
}) {
  const openManageSlates = useDialogs((s) => s.openManageSlates)
  if (total === 0) {
    return (
      <div className="rounded border border-dashed border-emerald-300 dark:border-emerald-800 p-8 flex flex-col items-center gap-2 text-center">
        <CheckCircle2 className="h-8 w-8 text-emerald-500" aria-hidden />
        <div className="text-sm font-medium text-zinc-700 dark:text-zinc-200">All clear</div>
        <div className="text-xs text-zinc-500">
          Every file loads and resolves — no invalid, hidden, or broken references found.
        </div>
      </div>
    )
  }
  return (
    <>
      <Section
        title="Invalid item files"
        icon={<FileWarning className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.invalidItems.length}
      >
        {data.invalidItems.map((v, i) => (
          <Row key={i} source={v.source} goSource={goSource}>
            <span className="text-red-600 dark:text-red-400">{v.reason}</span>
          </Row>
        ))}
      </Section>
      <Section
        title="Stray / hidden files"
        icon={<FileQuestion className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.strayFiles.length}
      >
        {data.strayFiles.map((s, i) => (
          <Row key={i} source={s.source} goSource={goSource}>
            <span className="text-red-600 dark:text-red-400">{s.reason}</span>
          </Row>
        ))}
      </Section>
      <BrokenTargets rows={data.linkTargets} goSource={goSource} push={push} qc={qc} />
      <Section
        title="Malformed item-link ids"
        icon={<AlertTriangle className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.malformedItemLinks.length}
      >
        {data.malformedItemLinks.map((m, i) => (
          <Row key={i} source={m.source} goSource={goSource}>
            <span className="text-zinc-500">{m.rel}</span> → target exists but id{' '}
            <code className="text-[11px]">{m.extractedId}</code> isn’t a valid NanoID{' '}
            <code className="text-[11px]">{m.targetPath}</code>
            <span className="text-zinc-500"> — fix by hand (no auto-relink)</span>
          </Row>
        ))}
      </Section>
      <Section
        title="Title / H1 drift"
        icon={<Type className="h-4 w-4 text-amber-500" aria-hidden />}
        count={data.titles.length}
      >
        {data.titles.map((t, i) => (
          <Row key={i} source={t.source} goSource={goSource}>
            <span className="text-zinc-500">frontmatter</span> “{t.frontmatterTitle}”{' '}
            <span className="text-zinc-500">≠ H1</span> “{t.bodyH1}”
          </Row>
        ))}
      </Section>
      <Section
        title="Hierarchy violations"
        icon={<Network className="h-4 w-4 text-amber-500" aria-hidden />}
        count={data.hierarchy.length}
      >
        {data.hierarchy.map((h, i) => (
          <Row key={i} source={h.source} goSource={goSource}>
            {h.childType} under{' '}
            {h.parentType ? (
              <>
                {h.parentType} <code className="text-[11px]">{h.parentId}</code>
              </>
            ) : (
              'no parent'
            )}
          </Row>
        ))}
      </Section>
      <Section
        title="Dangling ADR origins"
        icon={<Link2Off className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.origins.length}
      >
        {data.origins.map((o, i) => (
          <Row key={i} source={o.source} goSource={goSource}>
            origin → <code className="text-[11px]">{o.ref}</code>
          </Row>
        ))}
      </Section>
      <Section
        title="Duplicate KB ids"
        icon={<Copy className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.duplicateIds.length}
      >
        {data.duplicateIds.map((d, i) => (
          <li
            key={i}
            className="flex flex-col gap-1 rounded border border-zinc-200 dark:border-zinc-800 px-3 py-1.5 text-xs"
          >
            <span className="text-zinc-700 dark:text-zinc-300">
              {d.bucket} id <code className="text-[11px]">{d.id}</code>{' '}
              <span className="text-zinc-500">claimed by {d.paths.length} files</span>
            </span>
            <div className="flex flex-col gap-0.5">
              {d.paths.map((p, j) => (
                <button
                  key={j}
                  type="button"
                  onClick={() => goSource(p)}
                  title={`Open ${p}`}
                  className="self-start font-mono text-[11px] text-blue-600 dark:text-blue-400 hover:underline"
                >
                  {p}
                </button>
              ))}
            </div>
          </li>
        ))}
      </Section>
      <Section
        title="Stale identity fields"
        icon={<Fingerprint className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.identityDrift.length}
      >
        {data.identityDrift.map((d, i) => (
          <Row key={i} source={d.source} goSource={goSource}>
            <code className="text-[11px]">
              {d.field}: {d.declared}
            </code>{' '}
            <span className="text-zinc-500">but the filename says</span>{' '}
            <code className="text-[11px]">{d.expected}</code>
          </Row>
        ))}
        <li className="flex items-center gap-1 px-1 pt-0.5 text-[11px] text-zinc-500">
          Refs resolve by filename, so the declared value resolves nowhere — repair with
          <code className="text-[11px]">knit relink --ids</code>
          <CopyCommandIconButton value="knit relink --ids" />
        </li>
      </Section>
      <Section
        title="Health on non-project items"
        icon={<Activity className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.healthOnNonProject.length}
      >
        {data.healthOnNonProject.map((h, i) => (
          <Row key={i} source={h.source} goSource={goSource}>
            {h.type} <code className="text-[11px]">{h.itemId}</code> carries health{' '}
            <span className="text-red-600 dark:text-red-400">{h.health}</span>
            <span className="text-zinc-500">
              {' '}
              — health is project-only; clear it or retype the item to project
            </span>
          </Row>
        ))}
      </Section>
      <Section
        title="Bad project doc-folder pointers"
        icon={<FolderX className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.projectPointers.length}
      >
        {data.projectPointers.map((p, i) => (
          <Row key={i} source={p.source} goSource={goSource}>
            {p.type} <code className="text-[11px]">{p.itemId}</code> project ={' '}
            <code className="text-[11px]">{p.slug}</code>{' '}
            <span className="text-red-600 dark:text-red-400">{p.reason}</span>
          </Row>
        ))}
      </Section>
      <Section
        title="Malformed milestone sub-docs"
        icon={<Milestone className="h-4 w-4 text-red-500" aria-hidden />}
        count={data.malformedMilestones.length}
      >
        {data.malformedMilestones.map((m, i) => (
          <Row key={i} source={m.source} goSource={goSource}>
            <span className="text-red-600 dark:text-red-400">{m.reason}</span>
          </Row>
        ))}
      </Section>
      <Section
        title="Items with multiple milestones"
        icon={<Flag className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.multipleMilestones.length}
        warn
      >
        {data.multipleMilestones.map((m, i) => (
          <Row key={i} source={m.source} goSource={goSource}>
            <code className="text-[11px]">{m.itemId}</code> carries {m.count} Milestone rows
            <span className="text-zinc-500">
              {' '}
              — one per item is the convention; remove the extras
            </span>
          </Row>
        ))}
      </Section>
      <Section
        title="Cross-project milestone links"
        icon={<Flag className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.crossProjectMilestones.length}
      >
        {data.crossProjectMilestones.map((c, i) => (
          <Row key={i} source={c.source} goSource={goSource}>
            <code className="text-[11px]">{c.itemId}</code> ({c.itemProject}) links{' '}
            {c.milestoneProject}/{c.milestoneSlug}
            <span className="text-zinc-500">
              {' '}
              — informational: expected after `knit milestone move`
            </span>
          </Row>
        ))}
      </Section>
      <Section
        title="Done items with open children"
        icon={<ListTree className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.doneWithOpenChildren.length}
        warn
      >
        {data.doneWithOpenChildren.map((d, i) => (
          <Row key={i} source={d.source} goSource={goSource}>
            <code className="text-[11px]">{d.parentId}</code> (done) has open child{' '}
            <code className="text-[11px]">{d.childId}</code> ({d.childType}, {d.childState})
            <span className="text-zinc-500">
              {' '}
              — closing does not cascade; close the child or reopen the parent
            </span>
          </Row>
        ))}
      </Section>
      <Section
        title="Open items blocked by closed ones"
        icon={<Ban className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.staleBlockers.length}
        warn
      >
        {data.staleBlockers.map((b, i) => (
          <Row key={i} source={b.source} goSource={goSource}>
            <code className="text-[11px]">{b.itemId}</code> blocked by{' '}
            <code className="text-[11px]">{b.blockerId}</code> ({b.blockerState})
            <span className="text-zinc-500">
              {' '}
              — closing a blocker resolves nothing;{' '}
              {b.sole ? 'unblock the item' : 'remove the link'}, or point it at a new blocker
            </span>
          </Row>
        ))}
      </Section>
      <Section
        title="Slates not in the registry"
        icon={<Rows3 className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.unregisteredSlates.length}
        warn
      >
        {data.unregisteredSlates.map((u, i) => (
          <li
            key={i}
            className="flex flex-col gap-1 rounded border border-zinc-200 dark:border-zinc-800 px-3 py-1.5 text-xs"
          >
            <span className="text-zinc-700 dark:text-zinc-300">
              <code className="text-[11px]">{u.slate}</code>{' '}
              <span className="text-zinc-500">
                on {u.paths.length} item{u.paths.length === 1 ? '' : 's'} — add it in{' '}
                <button
                  type="button"
                  onClick={openManageSlates}
                  className="text-blue-600 dark:text-blue-400 hover:underline"
                >
                  Manage slates
                </button>{' '}
                to keep it, or remove it there to clear it from the items
              </span>
            </span>
            <div className="flex flex-col gap-0.5">
              {u.paths.map((p, j) => (
                <button
                  key={j}
                  type="button"
                  onClick={() => goSource(p)}
                  title={`Open ${p}`}
                  className="self-start font-mono text-[11px] text-blue-600 dark:text-blue-400 hover:underline"
                >
                  {p}
                </button>
              ))}
            </div>
          </li>
        ))}
      </Section>
      <Section
        title="Dangling prose refs"
        icon={<AlertTriangle className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.proseRefs.length}
        warn
      >
        {data.proseRefs.map((p, i) => (
          <Row key={i} source={p.source} goSource={goSource}>
            {PROSE_REF_LABEL[p.reason] ?? p.reason}{' '}
            <code className="text-[11px]">{p.ref}</code>
            {p.reason === 'short_id' && <span className="text-zinc-500"> — full id {p.candidates[0]}, `knit relink` expands it</span>}
            {p.reason === 'ambiguous_id' && <span className="text-zinc-500"> — matches {p.candidates.join(', ')}</span>}
            {p.reason === 'suffixed_anchor' && (
              <span className="text-zinc-500"> — it shifts when a same-named heading is added above; make the heading unique</span>
            )}
            {p.reason === 'reserved_anchor' && <span className="text-zinc-500"> — reads as a diagram pointer; rename the heading</span>}
          </Row>
        ))}
      </Section>
      <Section
        title="Dangling prose links"
        icon={<AlertTriangle className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.proseLinks.length}
        warn
      >
        {data.proseLinks.map((p, i) => (
          <Row key={i} source={p.source} goSource={goSource}>
            {p.kind} → <code className="text-[11px]">{p.target}</code>
            {p.reason === 'outside_repo' && (
              <span className="text-zinc-500"> — leaves the repo root and no mount covers it</span>
            )}
          </Row>
        ))}
      </Section>
      <Section
        title="KB sources pointing at nothing"
        icon={<AlertTriangle className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.sourcePaths.length}
        warn
      >
        {data.sourcePaths.map((s, i) => (
          <Row key={i} source={s.source} goSource={goSource}>
            <code className="text-[11px]">
              {s.mount ? `${s.mount}:` : ''}
              {s.path}
            </code>{' '}
            <span className="text-zinc-500">{s.reason}</span>
          </Row>
        ))}
      </Section>
      <Section
        title="SVG files to fix"
        icon={<AlertTriangle className="h-4 w-4 text-zinc-400" aria-hidden />}
        count={data.svgFiles.length}
        warn
      >
        {data.svgFiles.map((s, i) => (
          <Row key={i} source={s.source} goSource={goSource}>
            {s.reason}
          </Row>
        ))}
      </Section>
    </>
  )
}

const PROSE_REF_LABEL: Record<string, string> = {
  unresolved: 'unresolved ref',
  short_id: 'short id',
  ambiguous_id: 'ambiguous short id',
  missing_section: 'missing section',
  suffixed_anchor: 'anchor of a repeated heading',
  reserved_anchor: 'heading anchor taken by pointers',
}

/**
 * Mounts needing attention, matching `knit verify`; the full list lives
 * in settings. Read-only: repairing one means choosing between the committed
 * config and the per-machine override, so each row carries the fix command.
 */
function Mounts({ rows }: { rows: MountRow[] }) {
  return (
    <Section
      title="Mounts needing attention"
      icon={<HardDrive className="h-4 w-4 text-amber-500" aria-hidden />}
      count={rows.length}
      warn
    >
      {rows.map((m) => (
        <MountRowView key={m.name} row={m} />
      ))}
      <MountsHint />
    </Section>
  )
}

function BrokenTargets({
  rows,
  goSource,
  push,
  qc,
}: {
  rows: LinkTargetRow[]
  goSource: (source: string) => void
  push: ReturnType<typeof useToasts.getState>['push']
  qc: ReturnType<typeof useQueryClient>
}) {
  // Per-row in-flight guard so two repairs don't race the same scan.
  const [repairing, setRepairing] = useState<string | null>(null)

  const repair = async (row: LinkTargetRow) => {
    const key = `${row.source}|${row.targetPath}`
    setRepairing(key)
    try {
      const r = await doctorApi.relink(row.source, row.targetPath)
      if (r.repaired) {
        push('success', `Relinked → ${r.newTarget}`)
        await qc.invalidateQueries({ queryKey: doctorQk.verify })
      } else {
        push('info', 'No unique repair candidate found')
      }
    } catch (e) {
      push('error', `Relink failed: ${String(e)}`)
    } finally {
      setRepairing(null)
    }
  }

  return (
    <Section
      title="Broken link targets"
      icon={<Link2Off className="h-4 w-4 text-red-500" aria-hidden />}
      count={rows.length}
    >
      {rows.map((row, i) => {
        const key = `${row.source}|${row.targetPath}`
        return (
          <Row key={i} source={row.source} goSource={goSource}>
            <span className="text-zinc-500">{row.rel}</span> → broken target{' '}
            <code className="text-[11px]">{row.targetPath}</code>
            <button
              type="button"
              onClick={() => void repair(row)}
              disabled={repairing === key}
              title="Repair by relinking to the matching artifact"
              aria-label="Repair link"
              className="ml-2 inline-flex items-center gap-1 h-6 rounded border border-zinc-300 dark:border-zinc-700 px-1.5 text-[11px] font-medium text-zinc-700 dark:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-800 disabled:opacity-50"
            >
              {repairing === key ? (
                <Loader2 className="h-3 w-3 animate-spin" aria-hidden />
              ) : (
                <Wrench className="h-3 w-3" aria-hidden />
              )}
              Repair
            </button>
          </Row>
        )
      })}
    </Section>
  )
}

function Section({
  title,
  icon,
  count,
  warn,
  children,
}: {
  title: string
  icon: React.ReactNode
  count: number
  warn?: boolean
  children: React.ReactNode
}) {
  if (count === 0) return null
  return (
    <section>
      <h2 className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-zinc-600 dark:text-zinc-300 mb-2">
        {icon}
        {title}
        <span
          className={
            'rounded-full px-1.5 py-0.5 text-[10px] font-medium ' +
            (warn
              ? 'bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300'
              : 'bg-red-100 text-red-700 dark:bg-red-950/60 dark:text-red-300')
          }
        >
          {count}
        </span>
      </h2>
      <ul className="space-y-1">{children}</ul>
    </section>
  )
}

function Row({
  source,
  goSource,
  children,
}: {
  source: string
  goSource: (source: string) => void
  children: React.ReactNode
}) {
  return (
    <li className="flex flex-wrap items-center gap-x-2 gap-y-1 rounded border border-zinc-200 dark:border-zinc-800 px-3 py-1.5 text-xs">
      <button
        type="button"
        onClick={() => goSource(source)}
        title={`Open ${source}`}
        className="font-mono text-[11px] text-blue-600 dark:text-blue-400 hover:underline"
      >
        {source}
      </button>
      <span className="text-zinc-700 dark:text-zinc-300">{children}</span>
    </li>
  )
}
