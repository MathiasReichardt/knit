import { useEffect, useRef, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { ArrowRightCircle, SplitSquareHorizontal, RefreshCw, Trash2 } from 'lucide-react'
import { allowsGrowToFolder, kbApi, kbQk, type KbBucket, type KbEntry } from '../api/kb'
import { ApiError } from '../api/client'
import { EditableHeading } from './EditableHeading'
import { KbFrontmatterPanel, OpenFileButton, OpenLinkButton } from './KbFrontmatterPanel'
import { KbDeleteDialog } from './KbDeleteDialog'
import { MarkdownRender } from './MarkdownRender'
import { BodyEditor } from './BodyEditor'
import { ConflictDialog } from './ConflictDialog'
import { StaleBanner } from './StaleBanner'
import { useBodyEditorSession } from '../lib/useBodyEditorSession'
import { CopyIdIconButton, CopyPathIconButton, CopyRefIconButton } from './CopyButton'
import { CopyShareLinkButton } from './CopyShareLinkButton'
import { OpenInNewTabIconButton } from './OpenInNewTabButton'
import { useToasts } from '../stores/toasts'
import { bodyRef, resolveLink, parentFromAbsoluteUnderRepo } from '../lib/resolveLink'
import { followLink } from '../lib/linkCheck'
import { clearSearchHighlight } from '../lib/searchHighlight'
import { jumpToOccurrence } from '../lib/jumpToOccurrence'
import { useJumpIntent } from '../stores/jumpIntent'
import { useEditIntentEffect } from '../stores/editIntent'
import { editPlaceAt } from '../lib/editPlace'
import { DocOutline } from './DocOutline'

// Stable anchors on the KB entry, targeted by jump-to-occurrence.
const TITLE_REGION_ID = 'kb-region-title'
const BODY_REGION_ID = 'kb-region-body'

interface Props {
  bucket: KbBucket
  slug: string
}

export function KbDetails({ bucket, slug }: Props) {
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)
  const entry = useQuery({
    queryKey: kbQk.entry(bucket, slug),
    queryFn: () => kbApi.entry(bucket, slug),
  })

  if (entry.isLoading) return <div className="p-4 text-sm text-zinc-500">Loading…</div>
  if (entry.error) return <div className="p-4 text-sm text-red-600">Failed to load: {String(entry.error)}</div>
  if (!entry.data) return null

  return (
    <KbDetailsBody
      key={`${bucket}:${slug}`}
      entry={entry.data}
      // ['kb'] prefix (not just the entry) so a body edit that retitles via
      // the leading H1 also relabels the tree + lists.
      onSaved={() => qc.invalidateQueries({ queryKey: ['kb'] })}
      onPushToast={(k, m) => push(k, m)}
    />
  )
}

interface BodyProps {
  entry: KbEntry
  onSaved: () => void
  onPushToast: (kind: 'success' | 'error' | 'info', message: string) => void
}

function KbDetailsBody({ entry, onSaved, onPushToast }: BodyProps) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const session = useBodyEditorSession({
    fetchWithEtag: async () => {
      const fetched = await kbApi.fetchWithEtag(entry.bucket, entry.slug)
      return { body: fetched.entry.body, etag: fetched.etag }
    },
    patch: (body, ifMatch) =>
      kbApi.patch(entry.bucket, entry.slug, { body }, ifMatch).then(() => {}),
    onSaved,
    onReload: () => qc.invalidateQueries({ queryKey: kbQk.entry(entry.bucket, entry.slug) }),
    toast: onPushToast,
    currentBody: entry.body,
  })
  useEditIntentEffect(() => session.open())
  const scrollerRef = useRef<HTMLDivElement | null>(null)
  // Jump to a search pick's matched region (KB parity with items/designs).
  const intent = useJumpIntent((s) => s.intent)
  const consumeIntent = useJumpIntent((s) => s.consume)
  useEffect(() => {
    if (!intent || intent.kind !== 'kb' || intent.targetId !== entry.ref) return
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
  }, [intent, entry.ref, entry.body, consumeIntent])

  // Drop the word highlight when leaving the entry (keyed by ref → unmounts on switch).
  useEffect(() => () => clearSearchHighlight(), [])

  // Title, checkbox and diagram layout edits are instant, single-shot edits (no open editor to
  // protect), so they fetch the fresh etag and write in one go — the
  // capture-at-open conflict flow only guards the body editor.
  const quickPatch = async (patch: { body?: string; frontmatter?: KbEntry['frontmatter'] }) => {
    try {
      const fetched = await kbApi.fetchWithEtag(entry.bucket, entry.slug)
      await kbApi.patch(entry.bucket, entry.slug, patch, fetched.etag)
      onSaved()
      onPushToast('success', 'Saved')
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : String(e)
      onPushToast('error', `Save failed: ${msg}`)
    }
  }

  const saveTitle = (next: string) =>
    quickPatch({ frontmatter: { ...entry.frontmatter, title: next || null } })

  const linkParent = parentFromAbsoluteUnderRepo(entry.filePath, '.knit') ?? '.knit/kb'
  const onInternalLink = (href: string) => {
    const t = resolveLink(linkParent, href)
    void followLink(t, href, {
      navigate: (to) => navigate(to as never),
      toast: onPushToast,
    })
  }

  return (
    <div ref={scrollerRef} className="flex flex-col h-full min-h-0 overflow-y-auto">
      <div className="px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
        <div className="flex items-start gap-2">
          <div className="flex-1 min-w-0">
            <EditableHeading
              id={TITLE_REGION_ID}
              value={entry.title}
              editable={entry.bucket !== 'adr'}
              onCommit={(next) => void saveTitle(next)}
              className="text-lg font-semibold text-zinc-900 dark:text-zinc-100 truncate"
              ariaLabel="title"
            />
            <div className="text-xs font-mono text-zinc-500 flex items-center gap-1 mt-0.5">
              {entry.ref}
              <CopyIdIconButton value={entry.ref} title="Copy ref" />
              <CopyRefIconButton value={bodyRef(entry.ref)} title="Copy ref for body text" />
              <CopyPathIconButton value={entry.filePath} title="Copy file path" />
              <CopyShareLinkButton path={entry.filePath} />
              <OpenInNewTabIconButton />
            </div>
          </div>
          <KbActions
            entry={entry}
            onPushToast={onPushToast}
            onChanged={onSaved}
            onRebuildSuccess={(n) => {
              onPushToast('success', `Regenerated ${n} index file(s)`)
              void qc.invalidateQueries({ queryKey: ['kb'] })
            }}
          />
        </div>
        <KbFrontmatterPanel entry={entry} onPushToast={onPushToast} />
      </div>

      <section id={BODY_REGION_ID} className={
          // Capped only while the editor fills the pane: reading, the section is as
          // tall as the body, so the sticky edit pen travels all the way down.
          `group relative px-4 pt-4 pb-6 flex flex-col flex-1 ${session.editing ? 'min-h-0' : ''}`
        }
      >
        <DocOutline body={entry.body ?? ''} docRef={entry.ref} enabled={!session.editing} onEdit={session.editing ? undefined : () => session.open()} className={`flex-1 ${session.editing ? 'min-h-0' : ''}`}>
          {session.editing ? (
            <>
              {session.staleActive && (
                <StaleBanner onReload={() => void session.reloadStale()} onKeepMine={session.dismissStale} />
              )}
              <BodyEditor
                initialValue={session.seedBody ?? entry.body}
                onSave={session.save}
                onCancel={session.cancel}
                outline={{ docRef: entry.ref }}
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
                  docRef={entry.ref}
                  trackSection
                  linkParent={linkParent}
                  onInternalLink={onInternalLink}
                  onEditBody={(next) => quickPatch({ body: next })}
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
      {session.conflictActive && (
        <ConflictDialog
          shortId={entry.ref}
          busy={session.busy}
          onReload={session.reload}
          onOverwrite={() => void session.overwrite()}
          onKeepEditing={session.keepEditing}
        />
      )}
    </div>
  )
}

function KbActions({
  entry,
  onPushToast,
  onChanged,
  onRebuildSuccess,
}: {
  entry: KbEntry
  onPushToast: BodyProps['onPushToast']
  onChanged: () => void
  onRebuildSuccess: (filesWritten: number) => void
}) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [busy, setBusy] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const rebuild = useMutation({
    mutationFn: () => kbApi.rebuildIndex(),
    onSuccess: (r) => onRebuildSuccess(r.filesWritten.length),
    onError: (e) => onPushToast('error', `Failed: ${String(e)}`),
  })

  const rename = async () => {
    const newSlug = window.prompt(`Rename ${entry.ref} — new slug:`, entry.slug)
    if (!newSlug || newSlug.trim() === '' || newSlug === entry.slug) return
    setBusy('rename')
    try {
      const r = await kbApi.rename(entry.ref, newSlug.trim())
      const kbNote = r.kbEntriesAffected.length > 0 ? `, ${r.kbEntriesAffected.length} KB entries` : ''
      onPushToast('success', `Renamed → ${r.newRef} (${r.itemsRewritten.length} items rewritten${kbNote})`)
      void qc.invalidateQueries({ queryKey: ['kb'] })
      onChanged()
    } catch (e) {
      onPushToast('error', `Rename failed: ${String(e)}`)
    } finally {
      setBusy(null)
    }
  }

  const split = async () => {
    if (!confirm(`Split ${entry.ref} into a folder (slug.md → slug/readme.md)?`)) return
    setBusy('split')
    try {
      const r = await kbApi.split(entry.ref)
      onPushToast('success', `Split → ${r.newPath}`)
      // Force-refetch the tree rather than relying on the FS watcher
      // to surface the move (rename-into-folder fires two FS events
      // the watcher sometimes collapses).
      void qc.invalidateQueries({ queryKey: ['kb'] })
      void qc.refetchQueries({ queryKey: kbQk.tree })
      onChanged()
    } catch (e) {
      onPushToast('error', `Split failed: ${String(e)}`)
    } finally {
      setBusy(null)
    }
  }

  const supersede = async () => {
    const oldRef = window.prompt(
      `Supersede — old ADR ref (e.g. adr:0001-foo) that ${entry.ref} replaces:`,
      '',
    )
    if (!oldRef || oldRef.trim() === '') return
    setBusy('supersede')
    try {
      await kbApi.supersede(entry.ref, oldRef.trim())
      onPushToast('success', `Superseded ${oldRef}`)
      void qc.invalidateQueries({ queryKey: ['kb'] })
      onChanged()
    } catch (e) {
      onPushToast('error', `Supersede failed: ${String(e)}`)
    } finally {
      setBusy(null)
    }
  }

  const canSplit = allowsGrowToFolder(entry.bucket)
  const canSupersede = entry.bucket === 'adr'
  const isSource = entry.bucket === 'source'
  const sourceIsFile = entry.frontmatter.sourceKind === 'file'

  return (
    <div className="flex items-center gap-1 shrink-0">
      {isSource &&
        (sourceIsFile ? (
          <OpenFileButton path={entry.frontmatter.path} mount={entry.frontmatter.mount} />
        ) : (
          <OpenLinkButton url={entry.frontmatter.url} />
        ))}
      {confirmDelete && (
        <KbDeleteDialog
          entryRef={entry.ref}
          onClose={() => setConfirmDelete(false)}
          onDeleted={(result) => {
            setConfirmDelete(false)
            onPushToast(
              'success',
              `Deleted ${result.ref}${
                result.itemsAffected.length > 0 ? ` (${result.itemsAffected.length} item(s) updated)` : ''
              }`,
            )
            void qc.invalidateQueries({ queryKey: ['kb'] })
            void qc.refetchQueries({ queryKey: kbQk.tree })
            void navigate({ to: '/kb', search: { folder: '' } as never })
          }}
        />
      )}
      <button
        type="button"
        onClick={rename}
        disabled={busy !== null}
        title="Rename (rewrites incoming kb_refs across items)"
        aria-label="Rename"
        className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300 disabled:opacity-50"
      >
        <ArrowRightCircle className="h-4 w-4" />
      </button>
      {canSplit && (
        <button
          type="button"
          onClick={split}
          disabled={busy !== null}
          title="Split (promote to folder form)"
          aria-label="Split"
          className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300 disabled:opacity-50"
        >
          <SplitSquareHorizontal className="h-4 w-4" />
        </button>
      )}
      {canSupersede && (
        <button
          type="button"
          onClick={supersede}
          disabled={busy !== null}
          title="Supersede (wires supersede link both ways)"
          aria-label="Supersede"
          className="px-2 py-1 rounded text-xs text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300 disabled:opacity-50 font-medium"
        >
          Supersede…
        </button>
      )}
      <button
        type="button"
        onClick={() => rebuild.mutate()}
        title="Regenerate KB index files"
        aria-label="Regenerate KB index files"
        className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300 disabled:opacity-50"
        disabled={rebuild.isPending}
      >
        <RefreshCw className={'h-4 w-4 ' + (rebuild.isPending ? 'animate-spin' : '')} />
      </button>
      <button
        type="button"
        onClick={() => setConfirmDelete(true)}
        disabled={busy !== null}
        title="Delete (strips incoming kb_refs across items)"
        aria-label="Delete"
        className="p-1.5 rounded text-zinc-600 hover:bg-red-100 hover:text-red-700 dark:hover:bg-red-900/40 dark:text-zinc-300 disabled:opacity-50"
      >
        <Trash2 className="h-4 w-4" />
      </button>
    </div>
  )
}
