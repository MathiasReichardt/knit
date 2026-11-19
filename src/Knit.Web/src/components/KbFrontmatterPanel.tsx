import { useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Check,
  CircleArrowRight,
  CircleCheck,
  CircleDashed,
  CircleDot,
  CircleSlash,
  CircleX,
  ExternalLink,
  Eye,
  FileText,
  Link2,
  Tag,
  X,
} from 'lucide-react'
import type { IconCmp } from '../lib/glyphs'
import { kbApi, type KbBucket, type KbEntry, type KbFrontmatter } from '../api/kb'
import { mountBlockReason, mountsApi, mountsQk } from '../api/mounts'
import { MetaField, MetaFooter } from './MetaFields'
import { InlineChips } from './inline/InlineChips'
import { InlineEnum, type InlineEnumOption } from './inline/InlineEnum'
import { InlineText } from './inline/InlineText'
import { useKbTagVocabulary } from '../lib/useTagVocabulary'

interface Props {
  entry: KbEntry
  onPushToast: (kind: 'success' | 'error' | 'info', message: string) => void
}

const FEATURE_BUCKETS: ReadonlySet<KbBucket> = new Set(['adr', 'doc', 'log', 'idea'])
const FEATURE_LIST_BUCKETS: ReadonlySet<KbBucket> = new Set(['adr', 'log'])

// Hand-settable ADR statuses. `superseded` is terminal and comes from the
// supersede op (it pairs with superseded_by), so it's not offered.
const ADR_STATUS_OPTIONS = ['proposed', 'accepted', 'rejected', 'deprecated'] as const

// Value-encoding glyph for the ADR lifecycle — parity with item State:
// the icon changes with the value, so status reads at a glance. `superseded`
// is terminal (set by the supersede op).
const STATUS_ICON: Record<string, IconCmp> = {
  proposed: CircleDashed,
  accepted: CircleCheck,
  rejected: CircleX,
  deprecated: CircleSlash,
  superseded: CircleArrowRight,
}
const STATUS_COLOR: Record<string, string> = {
  proposed: 'text-zinc-400 dark:text-zinc-500',
  accepted: 'text-emerald-600 dark:text-emerald-400',
  rejected: 'text-red-600 dark:text-red-400',
  deprecated: 'text-zinc-500 dark:text-zinc-500',
  superseded: 'text-zinc-500 dark:text-zinc-500',
}

const STATUS_OPTIONS: InlineEnumOption<string>[] = ADR_STATUS_OPTIONS.map((s) => ({
  value: s,
  label: s,
  icon: STATUS_ICON[s],
  iconClass: STATUS_COLOR[s],
}))

function StatusGlyph({ status }: { status: string }) {
  const Icon = STATUS_ICON[status] ?? CircleDot
  return <Icon aria-hidden className={(STATUS_COLOR[status] ?? 'text-zinc-500') + ' h-4 w-4 shrink-0'} />
}

const TIP = {
  summary:
    "One-line description shown in `kb list` and agent context. Keep it concrete; an empty summary means the entry's ref is the only hint agents get.",
  feature:
    'Optional parent feature slug. Marks this entry as belonging to a feature so it surfaces when scoping context to that feature.',
  tags: 'Free-form labels for filtering (`kb list --tag`). Orthogonal to feature — use for cross-cutting themes.',
  status:
    'ADR lifecycle: proposed → accepted, then deprecated (retired, no replacement) or superseded (replaced by a named ADR, via supersede). rejected branches off proposed.',
  origin:
    'The design cycle / item this decision emerged from (design:<slug> or item:<id>). Distinct from feature: — each ref must resolve on save.',
} as const

export function KbFrontmatterPanel({ entry, onPushToast }: Props) {
  const qc = useQueryClient()
  const [busy, setBusy] = useState(false)
  const tagVocabulary = useKbTagVocabulary()
  const fm = entry.frontmatter
  const bucket = entry.bucket

  const patch = async (changes: Partial<KbFrontmatter>) => {
    setBusy(true)
    try {
      // Fetch the current etag immediately before the write — these
      // are single-field commits, so detect-and-toast is enough; the body
      // editor owns the richer reload/overwrite conflict flow.
      const fetched = await kbApi.fetchWithEtag(bucket, entry.slug)
      await kbApi.patch(bucket, entry.slug, { frontmatter: { ...fm, ...changes } }, fetched.etag)
      onPushToast('success', 'Saved')
      // ['kb'] prefix covers the entry query, tree, and lists.
      void qc.invalidateQueries({ queryKey: ['kb'] })
    } catch (e) {
      onPushToast('error', `Save failed: ${String(e)}`)
    } finally {
      setBusy(false)
    }
  }

  const showFeature = FEATURE_BUCKETS.has(bucket)
  const featureIsList = FEATURE_LIST_BUCKETS.has(bucket)
  const isSource = bucket === 'source'

  return (
    <div className="flex flex-col gap-2 mt-2">
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-2 text-sm">
        {/* Summary + Tags share the top row; tags wrap and grow the row's
            height when there are too many to fit on one line. */}
        <MetaField label="Summary" tooltip={TIP.summary} className="col-span-2 sm:col-span-2">
          <InlineText
            value={fm.summary}
            label="summary"
            emptyDisplay={<span className="italic text-zinc-400">(not set)</span>}
            disabled={busy}
            onCommit={(v) => void patch({ summary: v || null })}
          />
        </MetaField>

        <MetaField
          label="Tags"
          tooltip={TIP.tags}
          icon={Tag}
          className={'col-span-2 ' + (showFeature ? 'sm:col-span-1' : 'sm:col-span-2')}
        >
          <InlineChips
            values={fm.tags}
            label="tag"
            suggestions={tagVocabulary}
            busy={busy}
            emptyDisplay={<span className="text-xs italic text-zinc-400">(not set)</span>}
            onChange={(next) => void patch({ tags: next })}
          />
        </MetaField>

        {showFeature && (
          <MetaField
            label="Feature"
            tooltip={TIP.feature}
            className="col-span-2 sm:col-span-1"
          >
            {featureIsList ? (
              <InlineChips
                values={fm.feature}
                label="feature"
                busy={busy}
                emptyDisplay={<span className="text-xs italic text-zinc-400">(not set)</span>}
                onChange={(next) => void patch({ feature: next })}
              />
            ) : (
              <InlineText
                value={fm.feature[0] ?? null}
                label="feature"
                emptyDisplay={<span className="italic text-zinc-400">(not set)</span>}
                disabled={busy}
                onCommit={(v) => void patch({ feature: v ? [v] : [] })}
              />
            )}
          </MetaField>
        )}

        {/* Source: kind sits first and dictates which of url/path is the valid
            locator — so only that one is shown, on the same row. Changing kind
            swaps the locator and clears the old one (staged until committed). */}
        {isSource && <SourceFields fm={fm} busy={busy} onPatch={(c) => void patch(c)} />}

        {/* ADR lifecycle: status is an editable listbox, origin an editable
            ref-list (server-validated). Both are ADR-only fields. */}
        {bucket === 'adr' && (
          <MetaField label="Status" tooltip={TIP.status} className="col-span-2 sm:col-span-1">
            <StatusField
              value={fm.status}
              busy={busy}
              onCommit={(v) => void patch({ status: v })}
            />
          </MetaField>
        )}
        {bucket === 'adr' && (
          <MetaField label="Origin" tooltip={TIP.origin} icon={Link2} className="col-span-2 sm:col-span-3">
            <InlineChips
              values={fm.origin}
              label="origin"
              busy={busy}
              emptyDisplay={<span className="text-xs italic text-zinc-400">(not set)</span>}
              onChange={(next) => void patch({ origin: next })}
            />
          </MetaField>
        )}
      </div>

      {/* Read-only identity / lifecycle facts demoted to a muted footer
          rather than masquerading as editable grid cells. */}
      <MetaFooter
        items={[
          fm.date && <span>date {fm.date}</span>,
          fm.supersededBy && <span>superseded by {fm.supersededBy}</span>,
          fm.supersedes.length > 0 && <span>supersedes {fm.supersedes.join(', ')}</span>,
        ]}
      />
    </div>
  )
}

type SourceKind = 'link' | 'file'

const KIND_OPTIONS: InlineEnumOption<SourceKind>[] = [
  { value: 'link', label: 'link', icon: Link2 },
  { value: 'file', label: 'file', icon: FileText },
]

function SourceFields({
  fm,
  busy,
  onPatch,
}: {
  fm: KbFrontmatter
  busy: boolean
  onPatch: (changes: Partial<KbFrontmatter>) => void
}) {
  const currentKind: SourceKind = fm.sourceKind === 'file' ? 'file' : 'link'
  // A pending switch is staged locally and only written on commit, so cancel
  // restores the original kind *and* its locator with no round-trip.
  const [pendingKind, setPendingKind] = useState<SourceKind | null>(null)
  const effectiveKind = pendingKind ?? currentKind
  const isFile = effectiveKind === 'file'
  const oldLocator = isFile ? 'url' : 'path'

  const commitSwitch = (raw: string) => {
    const v = raw.trim()
    // An empty new locator is meaningless for a switch — treat it as cancel.
    if (!v || !pendingKind) {
      setPendingKind(null)
      return
    }
    onPatch(
      pendingKind === 'file'
        ? { sourceKind: 'file', path: v, url: null }
        : { sourceKind: 'link', url: v, path: null },
    )
    setPendingKind(null)
  }

  return (
    <>
      <MetaField label="Kind" icon={isFile ? FileText : Link2} className="col-span-2 sm:col-span-1">
        <InlineEnum
          value={effectiveKind}
          options={KIND_OPTIONS}
          label="source kind"
          disabled={busy}
          onChange={(next) => setPendingKind(next === currentKind ? null : next)}
        />
      </MetaField>

      {pendingKind ? (
        <MetaField label={isFile ? 'Path' : 'URL'} className="col-span-2 sm:col-span-3">
          <div className="flex flex-col gap-1">
            <SwitchLocatorInput
              label={isFile ? 'path' : 'url'}
              busy={busy}
              onCommit={commitSwitch}
              onCancel={() => setPendingKind(null)}
            />
            <span className="text-[11px] text-amber-600 dark:text-amber-500">
              Switching to {effectiveKind} clears the old {oldLocator}; cancel to keep it.
            </span>
          </div>
        </MetaField>
      ) : isFile ? (
        <MetaField label="Path" className="col-span-2 sm:col-span-3">
          <div className="flex flex-col gap-0.5 min-w-0">
            <div className="flex items-center gap-1 min-w-0">
              {fm.mount && <MountBadge mount={fm.mount} />}
              <InlineText
                value={fm.path}
                label="path"
                emptyDisplay={<span className="italic text-zinc-400">(not set)</span>}
                disabled={busy}
                onCommit={(v) => onPatch({ path: v || null })}
              />
            </div>
            {fm.mount && <MountReason mount={fm.mount} />}
          </div>
        </MetaField>
      ) : (
        <MetaField label="URL" className="col-span-2 sm:col-span-3">
          <InlineText
            value={fm.url}
            label="url"
            emptyDisplay={<span className="italic text-zinc-400">(not set)</span>}
            disabled={busy}
            onCommit={(v) => onPatch({ url: v || null })}
          />
        </MetaField>
      )}
    </>
  )
}

// The mount is config, not entry content, so it is shown and never edited
// here: `knit mount` declares it and `knit kb set <ref> mount=<name>` points
// an entry at it.
function MountBadge({ mount }: { mount: string }) {
  return (
    <span
      className="shrink-0 rounded bg-zinc-200 px-1 font-mono text-[11px] text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300"
      title={`Path is relative to mount '${mount}' (read-only)`}
    >
      {mount}
    </span>
  )
}

// An unresolved mount is a config problem, not a broken entry — so the reason
// sits on the locator, the entry stays listed, and only opening is disabled.
function MountReason({ mount }: { mount: string }) {
  const mounts = useQuery({ queryKey: mountsQk.list(), queryFn: mountsApi.list, staleTime: Infinity })
  const reason = mountBlockReason(mounts.data?.mounts, mount)
  if (!reason) return null
  return (
    <span className="text-[11px] text-amber-600 dark:text-amber-500">
      {reason} — see Doctor for the fix
    </span>
  )
}

// Open / preview a source live in the detail view's top action row, not next
// to the locator field: at field size they were too small to hit, and opening
// the source is a detail action like rename or delete, not a field edit.
const SOURCE_ACTION_CLASS =
  'p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300 shrink-0'

// Open the file source in the in-app previewer (`/file` route → `/api/file`),
// which reuses the path-scoping guard and CodeMirror syntax highlighting.
// A `mount` resolves the path against that config mount instead of the repo
// root; an unresolved one disables only this action — the entry is still fine.
export function OpenFileButton({ path, mount }: { path: string | null; mount?: string | null }) {
  const navigate = useNavigate()
  const mounts = useQuery({
    queryKey: mountsQk.list(),
    queryFn: mountsApi.list,
    staleTime: Infinity,
    enabled: !!mount,
  })
  if (!path) return null
  const blocked = mountBlockReason(mounts.data?.mounts, mount ?? null)
  if (blocked) {
    return (
      <button
        type="button"
        disabled
        title={`${blocked} — declare or repoint it with knit mount, then reload`}
        aria-label={`Cannot open file: ${blocked}`}
        className={`${SOURCE_ACTION_CLASS} opacity-50 cursor-not-allowed`}
      >
        <Eye className="h-4 w-4" />
      </button>
    )
  }
  return (
    <button
      type="button"
      onClick={() => void navigate({ to: '/file', search: mount ? { path, mount } : { path } })}
      title="Open file in previewer"
      aria-label="Open file in previewer"
      className={SOURCE_ACTION_CLASS}
    >
      <Eye className="h-4 w-4" />
    </button>
  )
}

// Link sources point outside the repo — open in a new tab; no in-app preview.
export function OpenLinkButton({ url }: { url: string | null }) {
  if (!url) return null
  return (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      title="Open link in new tab"
      aria-label="Open link in new tab"
      className={SOURCE_ACTION_CLASS}
    >
      <ExternalLink className="h-4 w-4" />
    </a>
  )
}

function SwitchLocatorInput({
  label,
  busy,
  onCommit,
  onCancel,
}: {
  label: string
  busy: boolean
  onCommit: (next: string) => void
  onCancel: () => void
}) {
  const [draft, setDraft] = useState('')
  // No commit-on-blur here: an explicit Cancel click would blur the input and
  // race the commit. Enter / the check button commit; Esc / ✕ cancel.
  return (
    <span className="inline-flex items-center gap-1 w-full">
      <input
        type="text"
        autoFocus
        value={draft}
        readOnly={busy}
        placeholder={`new ${label}`}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') { e.preventDefault(); onCommit(draft) }
          if (e.key === 'Escape') { e.preventDefault(); onCancel() }
        }}
        aria-label={`New ${label}`}
        className="bg-transparent border border-zinc-200 dark:border-zinc-800 rounded px-1.5 py-0.5 text-xs w-full hover:border-zinc-400 dark:hover:border-zinc-600 focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
      <button
        type="button"
        onClick={() => onCommit(draft)}
        title={`Save ${label}`}
        aria-label={`Save ${label}`}
        className="p-0.5 rounded text-zinc-500 hover:text-green-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 shrink-0"
      >
        <Check className="h-3 w-3" />
      </button>
      <button
        type="button"
        onClick={onCancel}
        title="Cancel switch"
        aria-label="Cancel switch"
        className="p-0.5 rounded text-zinc-500 hover:text-red-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 shrink-0"
      >
        <X className="h-3 w-3" />
      </button>
    </span>
  )
}

function StatusField({
  value,
  busy,
  onCommit,
}: {
  value: string | null
  busy: boolean
  onCommit: (next: string) => void
}) {
  const current = value ?? 'proposed'

  // superseded is terminal — set by the supersede op (pairs with superseded_by),
  // never hand-picked. Show it with no affordance at all, so it doesn't read as
  // editable.
  if (current === 'superseded') {
    return (
      <span className="inline-flex items-center gap-1.5 min-w-0 max-w-full" title={current}>
        <StatusGlyph status={current} />
        <span className="truncate">{current}</span>
      </span>
    )
  }

  const known = ADR_STATUS_OPTIONS.includes(current as (typeof ADR_STATUS_OPTIONS)[number])
  return (
    <InlineEnum
      value={known ? current : 'proposed'}
      options={STATUS_OPTIONS}
      label="status"
      disabled={busy}
      onChange={(next) => { if (next !== current) onCommit(next) }}
    >
      <StatusGlyph status={current} />
      <span className="truncate">{current}</span>
    </InlineEnum>
  )
}
