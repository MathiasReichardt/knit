import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { ArrowRightCircle, CalendarClock, CircleDashed, CircleCheck, CircleSlash, Flag, FolderInput, ListTree, Trash2 } from 'lucide-react'
import { projectsApi, projectsQk, type MilestoneDetail, type MilestonePatchFrontmatter } from '../api/projects'
import { ApiError } from '../api/client'
import { MarkdownRender } from './MarkdownRender'
import { BodyEditor } from './BodyEditor'
import { ConflictDialog } from './ConflictDialog'
import { StaleBanner } from './StaleBanner'
import { EditableHeading } from './EditableHeading'
import { InlineDate } from './inline/InlineDate'
import { InlineEnum, type InlineEnumOption } from './inline/InlineEnum'
import { MilestoneDeleteDialog } from './MilestoneDeleteDialog'
import { MilestoneMoveDialog } from './MilestoneMoveDialog'
import { CopyLinkIconButton, CopyPathIconButton } from './CopyButton'
import { CopyShareLinkButton } from './CopyShareLinkButton'
import { useBodyEditorSession } from '../lib/useBodyEditorSession'
import { bodyIsRenderEmpty } from '../lib/markdown'
import { useToasts } from '../stores/toasts'
import { milestoneDocLink } from '../lib/milestoneRef'
import { TYPE_COLOR, TYPE_ICON } from '../lib/glyphs'
import { formatShortDate } from '../lib/shortDate'
import type { ItemSummary } from '../api/types'
import { editPlaceAt } from '../lib/editPlace'
import { DocOutline } from './DocOutline'

// Value-encoding glyphs, as on item state — the icon changes with the value so
// a reached milestone reads at a glance in the header strip.
const STATE_OPTIONS: InlineEnumOption<string>[] = [
  { value: 'planned', label: 'planned', icon: CircleDashed, iconClass: 'text-zinc-500' },
  { value: 'reached', label: 'reached', icon: CircleCheck, iconClass: 'text-emerald-600 dark:text-emerald-400' },
  { value: 'dropped', label: 'dropped', icon: CircleSlash, iconClass: 'text-zinc-400' },
]

interface Props {
  project: string
  slug: string
  onSelectItem: (id: string) => void
}

// Right-pane view + authoring surface for a selected milestone sub-doc: inline
// title/state/target edits, the narrative body (shared BodyEditor), rename /
// delete / copy-link actions, and its backlinks (items carrying a Milestone row
// that resolves here — the reverse index). Docs are link targets, never
// sources, so there are no outgoing links to show.
export function MilestoneDetailPanel({ project, slug, onSelectItem }: Props) {
  const q = useQuery({
    queryKey: projectsQk.milestone(project, slug),
    queryFn: () => projectsApi.milestone(project, slug),
    retry: false,
  })

  if (q.isLoading) return <div className="p-4 text-sm text-zinc-500">Loading…</div>
  if (q.error || !q.data) {
    return (
      <div className="p-4 text-sm text-red-600 dark:text-red-400">
        Milestone not found: {project}/{slug}
      </div>
    )
  }

  return <MilestoneBody key={`${project}:${slug}`} m={q.data} onSelectItem={onSelectItem} />
}

function MilestoneBody({ m, onSelectItem }: { m: MilestoneDetail; onSelectItem: (id: string) => void }) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const push = useToasts((s) => s.push)
  const { project, slug } = m
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [moving, setMoving] = useState(false)

  const session = useBodyEditorSession({
    fetchWithEtag: async () => {
      const { detail, etag } = await projectsApi.fetchMilestoneWithEtag(project, slug)
      return { body: detail.body, etag }
    },
    patch: (body, ifMatch) =>
      projectsApi.patchMilestone(project, slug, { body }, ifMatch).then(() => {}),
    // ['projects'] prefix (not just the entry) so a body edit that retitles via
    // the leading H1 also relabels the tree + lists.
    onSaved: () => qc.invalidateQueries({ queryKey: ['projects'] }),
    onReload: () => qc.invalidateQueries({ queryKey: projectsQk.milestone(project, slug) }),
    toast: push,
    currentBody: m.body,
  })

  // Title/state/target are instant single-shot edits (no open editor to
  // protect): fetch the fresh etag and write in one go. The capture-at-open
  // conflict flow only guards the body editor.
  const quickPatch = async (fm: MilestonePatchFrontmatter) => {
    try {
      const { etag } = await projectsApi.fetchMilestoneWithEtag(project, slug)
      await projectsApi.patchMilestone(project, slug, { frontmatter: fm }, etag)
      void qc.invalidateQueries({ queryKey: ['projects'] })
      push('success', 'Saved')
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : String(e)
      push('error', `Save failed: ${msg}`)
    }
  }

  const rename = async () => {
    const next = window.prompt(`Rename milestone ${project}/${slug} — new slug:`, slug)
    if (!next || next.trim() === '' || next.trim() === slug) return
    try {
      const r = await projectsApi.renameMilestone(project, slug, next.trim())
      push('success', `Renamed → ${r.project}/${r.newSlug} (${r.repointedItemIds.length} item(s) re-pointed)`)
      void qc.invalidateQueries({ queryKey: ['projects'] })
      void navigate({ to: '/projects', search: { mp: r.project, ms: r.newSlug } as never })
    } catch (e) {
      push('error', `Rename failed: ${String(e)}`)
    }
  }

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
        <div className="flex items-center gap-2 text-xs text-zinc-500">
          <Flag className="h-3.5 w-3.5 text-cyan-600 dark:text-cyan-400" aria-hidden />
          <span className="font-mono">{project}/{slug}</span>
          <CopyLinkIconButton value={milestoneDocLink(slug)} title={`Copy roadmap link: ${milestoneDocLink(slug)}`} />
          <CopyPathIconButton value={m.filePath} title="Copy file path" />
          <CopyShareLinkButton path={m.filePath} />
          <span className="ml-auto inline-flex items-center gap-2">
            <StateEditor value={m.state} onCommit={(next) => void quickPatch({ state: next })} />
            <TargetEditor value={m.target} onCommit={(next) => void quickPatch({ target: next })} />
          </span>
        </div>
        <div className="mt-1 flex items-start gap-2">
          <EditableHeading
            value={m.title}
            onCommit={(next) => void quickPatch({ title: next })}
            className="text-lg font-semibold text-zinc-800 dark:text-zinc-100 truncate"
            ariaLabel="milestone title"
          />
          <div className="flex items-center gap-1 shrink-0">
            <button
              type="button"
              onClick={rename}
              title="Rename (re-points incoming Milestone links across items)"
              aria-label="Rename"
              className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300"
            >
              <ArrowRightCircle className="h-4 w-4" />
            </button>
            <button
              type="button"
              onClick={() => setMoving(true)}
              title="Move to another project (re-points incoming Milestone links across items)"
              aria-label="Move to another project"
              className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300"
            >
              <FolderInput className="h-4 w-4" />
            </button>
            <button
              type="button"
              onClick={() => setConfirmDelete(true)}
              title="Delete milestone"
              aria-label="Delete"
              className="p-1.5 rounded text-zinc-600 hover:bg-red-100 hover:text-red-700 dark:hover:bg-red-900/40 dark:text-zinc-300"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        </div>
      </div>

      <div className="flex-1 min-h-0 overflow-auto p-4 flex flex-col gap-4">
        <section className="group relative flex flex-col">
          <DocOutline body={m.body ?? ''} docRef={`.knit/projects/${project}/milestones/${slug}.md`} enabled={!session.editing} onEdit={session.editing ? undefined : () => session.open()}>
            {session.editing ? (
              <>
                {session.staleActive && (
                  <StaleBanner onReload={() => void session.reloadStale()} onKeepMine={session.dismissStale} />
                )}
                <BodyEditor
                  initialValue={session.seedBody ?? m.body}
                  onSave={session.save}
                  onCancel={session.cancel}
                  outline={{ docRef: `.knit/projects/${project}/milestones/${slug}.md` }}
                  initialPlace={session.openPlace}
                  busy={session.busy}
                  onDirtyChange={session.setEditorDirty}
                  reseedToken={session.reseedToken}
                />
              </>
            ) : (
              <div onDoubleClick={(e) => session.open(editPlaceAt(e.currentTarget, e.target, m.body ?? ''))}>
                {bodyIsRenderEmpty(m.body, m.title) ? (
                  <p className="text-sm italic text-zinc-500">No body content. Double-click to start writing.</p>
                ) : (
                  <MarkdownRender
                    landing={session.landing}
                    onLanded={session.clearLanding}
                    body={m.body}
                    title={m.title}
                    docRef={`.knit/projects/${project}/milestones/${slug}.md`}
                    linkParent={`.knit/projects/${project}/milestones`}
                    trackSection
                    onEditBody={session.quickSave}
                  />
                )}
              </div>
            )}
          </DocOutline>
        </section>

        <div>
          <div className="text-xs font-medium uppercase tracking-wide text-zinc-400 mb-1">
            Linked items ({m.backlinks.length})
          </div>
          {m.backlinks.length === 0 ? (
            <div className="text-sm text-zinc-500 italic">Nothing links this milestone yet.</div>
          ) : (
            <ul className="flex flex-col gap-0.5">
              {m.backlinks.map((b) => (
                <BacklinkRow key={b.id} item={b} onSelectItem={onSelectItem} />
              ))}
            </ul>
          )}
        </div>
      </div>

      {session.conflictActive && (
        <ConflictDialog
          shortId={`${project}/${slug}`}
          busy={session.busy}
          onReload={session.reload}
          onOverwrite={() => void session.overwrite()}
          onKeepEditing={session.keepEditing}
        />
      )}
      {moving && (
        <MilestoneMoveDialog
          project={project}
          slug={slug}
          title={m.title}
          onClose={() => setMoving(false)}
          onMoved={(r) => {
            setMoving(false)
            void navigate({ to: '/projects', search: { mp: r.toProject, ms: r.newSlug } as never })
          }}
        />
      )}
      {confirmDelete && (
        <MilestoneDeleteDialog
          project={project}
          slug={slug}
          title={m.title}
          onClose={() => setConfirmDelete(false)}
          onDeleted={() => {
            setConfirmDelete(false)
            void navigate({ to: '/projects', search: {} as never })
          }}
        />
      )}
    </div>
  )
}

// State and target: the value itself opens its editor (22) — no pen, and the
// popovers float, so the header strip never reflows when one is opened.
function StateEditor({ value, onCommit }: { value: string; onCommit: (next: string) => void }) {
  const cls =
    value === 'reached' ? 'text-emerald-600 dark:text-emerald-400'
      : value === 'dropped' ? 'text-zinc-400 line-through'
        : 'text-zinc-500'
  return (
    <InlineEnum
      value={value}
      options={STATE_OPTIONS}
      label="milestone state"
      onChange={onCommit}
    >
      <span className={`text-xs ${cls}`}>{value}</span>
    </InlineEnum>
  )
}

function TargetEditor({ value, onCommit }: { value: string | null; onCommit: (next: string) => void }) {
  const target = formatShortDate(value)
  return (
    <InlineDate value={value} label="milestone target date" onChange={onCommit}>
      {target
        ? <span className="inline-flex items-center gap-1 text-zinc-500" title={target.full}>
            <CalendarClock className="h-3 w-3 shrink-0" aria-hidden />{target.short}
          </span>
        : <span className="text-zinc-400">—</span>}
    </InlineDate>
  )
}

function BacklinkRow({ item, onSelectItem }: { item: ItemSummary; onSelectItem: (id: string) => void }) {
  const navigate = useNavigate()
  const Icon = TYPE_ICON[item.type]
  return (
    <li className="flex items-center gap-2 rounded px-1.5 py-1 hover:bg-zinc-100 dark:hover:bg-zinc-900">
      <button
        type="button"
        onClick={() => onSelectItem(item.id)}
        className="flex items-center gap-2 min-w-0 flex-1 text-left"
      >
        <Icon className={`${TYPE_COLOR[item.type]} h-3.5 w-3.5 shrink-0`} aria-hidden />
        <span className="truncate text-sm">{item.title}</span>
      </button>
      <button
        type="button"
        onClick={() => void navigate({ to: '/items', search: { id: item.id } as never })}
        title="Open in items tree"
        aria-label="Open in items tree"
        className="shrink-0 p-1 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
      >
        <ListTree className="h-3.5 w-3.5" />
      </button>
    </li>
  )
}
