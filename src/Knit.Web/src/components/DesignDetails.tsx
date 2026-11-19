import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive } from 'lucide-react'
import { designsApi, designsQk, type DesignDoc } from '../api/designs'
import { MarkdownRender } from './MarkdownRender'
import { BodyEditor } from './BodyEditor'
import { ConflictDialog } from './ConflictDialog'
import { StaleBanner } from './StaleBanner'
import { CopyIdIconButton, CopyPathIconButton, CopyRefIconButton } from './CopyButton'
import { CopyShareLinkButton } from './CopyShareLinkButton'
import { OpenInNewTabIconButton } from './OpenInNewTabButton'
import { bodyRef, parentFromAbsoluteUnderRepo } from '../lib/resolveLink'
import { DesignArchiveDialog } from './DesignArchiveDialog'
import { MetaFooter } from './MetaFields'
import { formatShortDate } from '../lib/shortDate'
import { useToasts } from '../stores/toasts'
import { clearSearchHighlight } from '../lib/searchHighlight'
import { jumpToOccurrence } from '../lib/jumpToOccurrence'
import { useJumpIntent } from '../stores/jumpIntent'
import { useEditIntentEffect } from '../stores/editIntent'
import { useBodyEditorSession } from '../lib/useBodyEditorSession'
import { editPlaceAt } from '../lib/editPlace'
import { DocOutline } from './DocOutline'

// Stable anchors on the design doc, targeted by jump-to-occurrence.
const TITLE_REGION_ID = 'design-region-title'
const BODY_REGION_ID = 'design-region-body'

interface Props {
  slug: string
  onArchived: () => void
}

export function DesignDetails({ slug, onArchived }: Props) {
  const entry = useQuery({
    queryKey: designsQk.entry(slug),
    queryFn: () => designsApi.get(slug),
  })

  if (entry.isLoading) return <div className="p-4 text-sm text-zinc-500">Loading…</div>
  if (entry.error) return <div className="p-4 text-sm text-red-600">Failed to load: {String(entry.error)}</div>
  if (!entry.data) return null

  return (
    <DesignDetailsBody
      key={slug}
      entry={entry.data}
      onArchived={onArchived}
    />
  )
}

function DesignDetailsBody({
  entry,
  onArchived,
}: {
  entry: DesignDoc
  onArchived: Props['onArchived']
}) {
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)
  const [confirmArchive, setConfirmArchive] = useState(false)

  const session = useBodyEditorSession({
    fetchWithEtag: async () => {
      const fetched = await designsApi.fetchWithEtag(entry.slug)
      return { body: fetched.doc.body, etag: fetched.etag }
    },
    patch: (body, ifMatch) => designsApi.patch(entry.slug, body, ifMatch).then(() => {}),
    // ['designs'] prefix (not just the entry) so a body edit that retitles
    // via the leading H1 also relabels the tree list.
    onSaved: () => qc.invalidateQueries({ queryKey: ['designs'] }),
    onReload: () => qc.invalidateQueries({ queryKey: designsQk.entry(entry.slug) }),
    toast: push,
    currentBody: entry.body,
  })
  useEditIntentEffect(() => session.open())
  const scrollerRef = useRef<HTMLDivElement | null>(null)
  // Archived designs stay editable (amend finished docs) but can't be archived
  // again — only the archive action is gated on `completed`.
  const canArchive = !entry.completed

  // Jump to a search pick's matched region (design parity with items):
  // highlight the matched word in the body, falling back to snapping the body
  // offset to its section heading. A title-only hit opens the doc as-is.
  const intent = useJumpIntent((s) => s.intent)
  const consumeIntent = useJumpIntent((s) => s.consume)
  useEffect(() => {
    if (!intent || intent.kind !== 'design' || intent.targetId !== entry.slug) return
    const captured = intent
    const raf = requestAnimationFrame(() => {
      const root = scrollerRef.current
      if (root) jumpToOccurrence(root, entry.body, captured, {
        bodyRegionId: BODY_REGION_ID,
        titleRegionId: TITLE_REGION_ID,
      })
      consumeIntent()
    })
    return () => cancelAnimationFrame(raf)
  }, [intent, entry.slug, entry.body, consumeIntent])

  // Drop the word highlight when leaving the doc (keyed by slug → unmounts on switch).
  useEffect(() => () => clearSearchHighlight(), [])

  const archive = useMutation({
    mutationFn: () => designsApi.archive(entry.slug),
    onSuccess: (r) => {
      push('success', `Archived → ${r.newPath}`)
      setConfirmArchive(false)
      void qc.invalidateQueries({ queryKey: ['designs'] })
      onArchived()
    },
    onError: (e) => push('error', `Archive failed: ${String(e)}`),
  })

  return (
    <div ref={scrollerRef} className="flex flex-col h-full min-h-0 overflow-y-auto">
      {confirmArchive && (
        <DesignArchiveDialog
          slug={entry.slug}
          busy={archive.isPending}
          onConfirm={() => archive.mutate()}
          onCancel={() => setConfirmArchive(false)}
        />
      )}
      {session.conflictActive && (
        <ConflictDialog
          shortId={`design:${entry.slug}`}
          busy={session.busy}
          onReload={session.reload}
          onOverwrite={() => void session.overwrite()}
          onKeepEditing={session.keepEditing}
        />
      )}
      <div className="px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
        <div className="flex items-start gap-2">
          <div className="flex-1 min-w-0">
            <h1 id={TITLE_REGION_ID} className="text-lg font-semibold text-zinc-900 dark:text-zinc-100 truncate">
              {entry.title}
              {entry.completed && (
                <span className="ml-2 align-middle text-xs font-medium px-1.5 py-0.5 rounded bg-zinc-200 dark:bg-zinc-700 text-zinc-600 dark:text-zinc-300">
                  completed
                </span>
              )}
            </h1>
            <div className="text-xs font-mono text-zinc-500 flex items-center gap-1 mt-0.5">
              <span className="truncate">{entry.filePath}</span>
              <CopyIdIconButton value={`design:${entry.slug}`} title="Copy ref" />
              <CopyRefIconButton value={bodyRef(`design:${entry.slug}`)} title="Copy ref for body text" />
              <CopyPathIconButton value={entry.filePath} title="Copy file path" />
              <CopyShareLinkButton path={entry.filePath} />
              <OpenInNewTabIconButton />
            </div>
          </div>
          {canArchive && (
            <button
              type="button"
              onClick={() => setConfirmArchive(true)}
              disabled={archive.isPending}
              title="Archive (move into completed/<slug>/)"
              aria-label="Archive"
              className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300 disabled:opacity-50 shrink-0"
            >
              <Archive className="h-4 w-4" />
            </button>
          )}
        </div>
        {/* Provenance parity with the item / KB surfaces. Designs
            carry no `created`, so only the edited timestamp is shown. */}
        {(() => {
          const edited = formatShortDate(entry.modified)
          return (
            <div className="mt-1.5">
              <MetaFooter items={[edited && <span title={edited.full}>edited {edited.short}</span>]} />
            </div>
          )
        })()}
      </div>

      <section id={BODY_REGION_ID} className={
          // Capped only while the editor fills the pane: reading, the section is as
          // tall as the body, so the sticky edit pen travels all the way down.
          `group relative px-4 pt-4 pb-6 flex flex-col flex-1 ${session.editing ? 'min-h-0' : ''}`
        }
      >
        <DocOutline body={entry.body ?? ''} docRef={`design:${entry.slug}`} enabled={!session.editing} onEdit={session.editing ? undefined : () => session.open()} className={`flex-1 ${session.editing ? 'min-h-0' : ''}`}>
          {session.editing ? (
            <>
              {session.staleActive && (
                <StaleBanner onReload={() => void session.reloadStale()} onKeepMine={session.dismissStale} />
              )}
              <BodyEditor
                initialValue={session.seedBody ?? entry.body}
                onSave={session.save}
                onCancel={session.cancel}
                outline={{ docRef: `design:${entry.slug}` }}
                initialPlace={session.openPlace}
                busy={session.busy}
                onDirtyChange={session.setEditorDirty}
                reseedToken={session.reseedToken}
                fill
              />
            </>
          ) : (
            <div onDoubleClick={(e) => session.open(editPlaceAt(e.currentTarget, e.target, entry.body ?? ''))}>
              {entry.body?.trim() ? (
                <MarkdownRender
                  landing={session.landing}
                  onLanded={session.clearLanding}
                  body={entry.body}
                  title={entry.title}
                  docRef={`design:${entry.slug}`}
                  linkParent={parentFromAbsoluteUnderRepo(entry.filePath, '.knit') ?? '.knit/designs'}
                  trackSection
                  onEditBody={session.quickSave}
                />
              ) : (
                <p className="text-sm italic text-zinc-500">
                  No body content. Double-click to start writing.
                </p>
              )}
            </div>
          )}
        </DocOutline>
      </section>
    </div>
  )
}
