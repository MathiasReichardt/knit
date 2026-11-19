import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowRightLeft,
  Ban,
  CheckCircle2,
  CircleDot,
  Glasses,
  Link2,
  MessageSquare,
  MoreHorizontal,
  Move,
  Pencil,
  Play,
  Plus,
  RotateCcw,
  Stamp,
  Trash2,
  Undo2,
  UserPlus,
  XCircle,
} from 'lucide-react'
import { ApiError, api, qk } from '../api/client'
import { MarkdownRender } from './MarkdownRender'
import { EditableHeading } from './EditableHeading'
import { CopyIdIconButton, CopyPathIconButton, CopyRefIconButton } from './CopyButton'
import { CopyShareLinkButton } from './CopyShareLinkButton'
import { OpenInNewTabIconButton } from './OpenInNewTabButton'
import { RowLink } from './RowLink'
import { ItemDeleteDialog } from './ItemDeleteDialog'
import { FrontmatterStrip } from './FrontmatterStrip'
import { ContextPanel } from './ContextPanel'
import { LinksTable } from './LinksTable'
import { CommentsList } from './CommentsList'
import { BodyEditor } from './BodyEditor'
import { AddLinkDialog, ItemPicker } from './AddLinkDialog'
import { EditLinkDialog } from './EditLinkDialog'
import { CollapsibleSection } from './CollapsibleSection'
import { CommentComposer } from './CommentComposer'
import { ConflictDialog } from './ConflictDialog'
import { StaleBanner } from './StaleBanner'
import { MoveParentDialog, RetypeDialog } from './StructuralDialogs'
import { useToasts } from '../stores/toasts'
import { useBlockedDependents } from '../stores/blockedDependents'
import { STATE_COLOR, STATE_ICON, TYPE_COLOR, TYPE_ICON } from '../lib/glyphs'
import { stateActions } from '../lib/stateActions'
import type { Item, LinkRow } from '../api/types'
import { patchItemSafe } from '../lib/patchItem'
import { useBodyEditorSession } from '../lib/useBodyEditorSession'
import { useEditIntentEffect } from '../stores/editIntent'
import { bodyRef, resolveLink } from '../lib/resolveLink'
import { followLink } from '../lib/linkCheck'
import { useJumpIntent } from '../stores/jumpIntent'
import { useSectionOpen } from '../stores/sectionOpen'
import { clearSearchHighlight } from '../lib/searchHighlight'
import { jumpToOccurrence } from '../lib/jumpToOccurrence'
import { editPlaceAt } from '../lib/editPlace'
import { EditBodyButton } from './EditBodyButton'
import { DocOutline, type OutlineTail } from './DocOutline'

// Stable anchor ids on the details regions, targeted by jump-to-occurrence.
const TITLE_REGION_ID = 'details-region-title'
const BODY_REGION_ID = 'details-region-body'
const COMMENTS_REGION_ID = 'details-region-comments'
const LINKS_REGION_ID = 'details-region-links'
// An item gets the outline only once its body is long enough to need one.
const OUTLINE_MIN_HEADINGS = 3
// Links + Comments: always listed, they count toward the minimum.
const OUTLINE_TAIL_SECTIONS = 2
// Item md files live flat here, so body hrefs resolve against it.
const ITEMS_DIR = '.knit/items'

interface Props {
  itemId: string | null
  onSelectItem: (id: string) => void
}

export function DetailsPane({ itemId, onSelectItem }: Props) {
  if (!itemId) {
    return (
      <div className="h-full flex items-center justify-center p-8 text-center">
        <div className="text-sm text-zinc-500 dark:text-zinc-400 max-w-xs">
          <p className="font-medium mb-1">No item selected</p>
          <p>Pick one from the tree on the left.</p>
        </div>
      </div>
    )
  }
  // Key on itemId so all transient state (open dialogs, body edit mode,
  // comment composer) resets when the user switches items. Otherwise a
  // half-finished body edit would silently retarget the wrong item.
  return <DetailsBody key={itemId} itemId={itemId} onSelectItem={onSelectItem} />
}

function DetailsBody({ itemId, onSelectItem }: { itemId: string; onSelectItem: (id: string) => void }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const q = useQuery({ queryKey: qk.item(itemId), queryFn: () => api.item(itemId) })

  // Jump to the region a search pick matched in. Runs once the item data is
  // present (the `q.data` dep fires after a fresh navigation loads) and again
  // whenever a new intent arrives (the nonce makes re-picking replay). The
  // jump is deferred to the next frame so an expanded section has committed,
  // then the intent is consumed so returning to the item later doesn't re-flash.
  const scrollRef = useRef<HTMLDivElement>(null)
  const [editingBody, setEditingBody] = useState(false)
  const outlineTail = useMemo<OutlineTail[]>(
    () => [
      { id: LINKS_REGION_ID, text: 'Links', icon: Link2, reveal: () => useSectionOpen.getState().setOpen('details.links', true) },
      { id: COMMENTS_REGION_ID, text: 'Comments', icon: MessageSquare, reveal: () => useSectionOpen.getState().setOpen('details.comments', true) },
    ],
    [],
  )
  const intent = useJumpIntent((s) => s.intent)
  const consumeIntent = useJumpIntent((s) => s.consume)
  useEffect(() => {
    const item = q.data
    if (!item || !intent || intent.kind !== 'item' || intent.targetId !== item.id) return
    if (intent.field === 'comment') useSectionOpen.getState().setOpen('details.comments', true)
    const captured = intent
    const raf = requestAnimationFrame(() => {
      const root = scrollRef.current
      if (root) jumpToOccurrence(root, item.body ?? '', captured, {
        bodyRegionId: BODY_REGION_ID,
        commentsRegionId: COMMENTS_REGION_ID,
        titleRegionId: TITLE_REGION_ID,
      })
      consumeIntent()
    })
    return () => cancelAnimationFrame(raf)
  }, [intent, q.data, consumeIntent])

  // Drop the word highlight when leaving the item (DetailsBody is keyed by
  // itemId, so this unmounts on every switch). Re-picking re-paints it.
  useEffect(() => () => clearSearchHighlight(), [])

  if (q.isLoading) return <div className="p-6 text-sm text-zinc-500">Loading item…</div>
  if (q.error) return <div className="p-6 text-sm text-red-600">Failed to load item</div>
  const item = q.data
  if (!item) return null

  const saveTitle = async (next: string) => {
    if (next === item.title) return
    const res = await patchItemSafe(qc, item.id, { title: next })
    if (!res.ok) {
      toast('error', `Rename failed: ${res.error?.problem?.detail ?? res.error?.message ?? ''}`)
      return
    }
    toast('success', `Renamed: ${item.shortId}`)
  }

  const TypeIcon = TYPE_ICON[item.type]
  const StateIcon = STATE_ICON[item.state]
  return (
    <div ref={scrollRef} className="h-full overflow-y-auto">
      <div className="px-5 py-4 border-b border-zinc-200 dark:border-zinc-800 bg-zinc-100 dark:bg-zinc-900/70 space-y-3">
        {/* flex-wrap: when the pane is narrow the action buttons drop below
            the title instead of squeezing it into a sliver. basis-56
            guarantees the title group a readable width before wrapping. */}
        <div className="flex flex-wrap items-start gap-x-3 gap-y-2">
          <div className="flex items-start gap-3 grow basis-56 min-w-0">
            <div className="flex items-center gap-1.5 pt-0.5">
              <TypeIcon
                aria-label={item.type}
                className={TYPE_COLOR[item.type] + ' h-5 w-5 shrink-0'}
              />
              <StateIcon
                aria-label={item.state}
                className={STATE_COLOR[item.state] + ' h-5 w-5 shrink-0'}
              />
            </div>
            <EditableHeading
              id={TITLE_REGION_ID}
              value={item.title}
              onCommit={(next) => void saveTitle(next)}
              className="text-lg font-semibold text-zinc-900 dark:text-zinc-100 min-w-0 break-words"
              ariaLabel="title"
            />
          </div>
          <ActionMenu item={item} />
        </div>
        <div className="flex items-center gap-2 text-xs">
          <span className="font-mono text-zinc-400 dark:text-zinc-600 truncate" title={item.id}>
            {item.shortId}
          </span>
          <CopyIdIconButton value={item.id} title="Copy full id" />
          <CopyRefIconButton value={bodyRef(`item:${item.id}`, item.title)} title="Copy ref for body text" />
          <CopyPathIconButton value={item.filePath} />
          <CopyShareLinkButton path={item.filePath} />
          <OpenInNewTabIconButton />
        </div>
        <FrontmatterStrip item={item} />
        {(item.blocked || item.heldBy) && <BlockedStripe item={item} onSelectItem={onSelectItem} />}
      </div>

      <DocOutline
        body={item.body ?? ''}
        docRef={`item:${item.id}`}
        enabled={!editingBody}
        minHeadings={OUTLINE_MIN_HEADINGS}
        tail={outlineTail}
        className="pb-4"
      >
        <BodySection item={item} onSelectItem={onSelectItem} onEditingChange={setEditingBody} />
        <LinksSection item={item} onSelectItem={onSelectItem} />
        <CommentsSection item={item} />
        <ContextPanel itemId={item.id} onSelectItem={onSelectItem} />
      </DocOutline>
    </div>
  )
}

function BodySection({
  item,
  onSelectItem,
  onEditingChange,
}: {
  item: Item
  onSelectItem: (id: string) => void
  onEditingChange: (editing: boolean) => void
}) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const navigate = useNavigate()

  const session = useBodyEditorSession({
    fetchWithEtag: async () => {
      const fetched = await api.fetchItemWithEtag(item.id)
      return { body: fetched.item.body ?? '', etag: fetched.etag }
    },
    patch: async (body, ifMatch) => {
      const res = await api.patchItem(item.id, { body }, ifMatch)
      qc.setQueryData(qk.item(item.id), res.item)
    },
    onSaved: () => {
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
    },
    onReload: () => qc.invalidateQueries({ queryKey: qk.item(item.id) }),
    toast,
    currentBody: item.body ?? '',
  })
  useEditIntentEffect(() => session.open())
  useEffect(() => onEditingChange(session.editing), [session.editing, onEditingChange])

  // Item md files live flat in `.knit/items/`, so hrefs in the body
  // resolve against that directory. The shared resolver handles sibling
  // items, kb refs, and arbitrary repo files.
  const onInternalLink = (href: string) => {
    const t = resolveLink(ITEMS_DIR, href)
    void followLink(t, href, {
      navigate: (to) => navigate(to as never),
      toast,
      onItem: onSelectItem,
    })
  }

  // Checkbox toggles and diagram layout edits are instant, derived edits (no
  // open editor to protect), so they fetch the fresh etag and write in one
  // shot — the capture-at-open conflict flow only applies to the body editor.
  const onEditBody = async (next: string) => {
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.patchItem(item.id, { body: next }, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      toast('success', `Saved: ${item.shortId}`)
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'save failed'
      toast('error', `Save failed: ${msg}`)
    }
  }

  return (
    <section id={BODY_REGION_ID} className="group relative px-5 pt-4">
      {!session.editing && <EditBodyButton onClick={() => session.open()} />}
      {session.editing ? (
        <>
          {session.staleActive && (
            <StaleBanner onReload={() => void session.reloadStale()} onKeepMine={session.dismissStale} />
          )}
          <BodyEditor
            initialValue={session.seedBody ?? item.body}
            onSave={session.save}
            onCancel={session.cancel}
            initialPlace={session.openPlace}
            busy={session.busy}
            onDirtyChange={session.setEditorDirty}
            reseedToken={session.reseedToken}
            // The editor lists no Links / Comments rows; the same body still gets its outline.
            outline={{ docRef: `item:${item.id}`, minHeadings: OUTLINE_MIN_HEADINGS - OUTLINE_TAIL_SECTIONS }}
          />
        </>
      ) : (
        <div onDoubleClick={(e) => session.open(editPlaceAt(e.currentTarget, e.target, item.body ?? ''))}>
          {item.body?.trim() ? (
            <MarkdownRender
              landing={session.landing}
              onLanded={session.clearLanding}
              body={item.body}
              title={item.title}
              docRef={`item:${item.id}`}
              trackSection
              linkParent={ITEMS_DIR}
              onInternalLink={onInternalLink}
              onEditBody={onEditBody}
            />
          ) : (
            <p className="text-sm italic text-zinc-500">
              No body content. Double-click to start writing.
            </p>
          )}
        </div>
      )}
      {session.conflictActive && (
        <ConflictDialog
          shortId={item.shortId}
          busy={session.busy}
          onReload={session.reload}
          onOverwrite={() => void session.overwrite()}
          onKeepEditing={session.keepEditing}
        />
      )}
    </section>
  )
}

function LinksSection({ item, onSelectItem }: { item: Item; onSelectItem: (id: string) => void }) {
  const [adding, setAdding] = useState(false)
  return (
    <>
      <CollapsibleSection
        icon={Link2}
        id={LINKS_REGION_ID}
        label={`Links (${item.links.length})`}
        persistKey="details.links"
        actions={
          <button
            type="button"
            onClick={() => setAdding(true)}
            title="Add link"
            aria-label="Add link"
            className="text-xs px-1.5 py-0.5 rounded border border-zinc-200 dark:border-zinc-800 hover:bg-zinc-100 dark:hover:bg-zinc-900"
          >
            +
          </button>
        }
      >
        <LinksTable item={item} onSelectItem={onSelectItem} />
      </CollapsibleSection>
      {adding && <AddLinkDialog item={item} onClose={() => setAdding(false)} />}
    </>
  )
}

function CommentsSection({ item }: { item: Item }) {
  const [composing, setComposing] = useState(false)
  return (
    <CollapsibleSection
      icon={MessageSquare}
      id={COMMENTS_REGION_ID}
      label={`Comments (${item.comments.length})`}
      persistKey="details.comments"
      actions={
        <button
          type="button"
          onClick={() => setComposing(true)}
          title="Add comment"
          aria-label="Add comment"
          className="text-xs px-1.5 py-0.5 rounded border border-zinc-200 dark:border-zinc-800 hover:bg-zinc-100 dark:hover:bg-zinc-900"
        >
          +
        </button>
      }
    >
      <CommentsList item={item} />
      {composing && (
        <div className="mt-2">
          <CommentComposer item={item} onClose={() => setComposing(false)} />
        </div>
      )}
    </CollapsibleSection>
  )
}

function ActionMenu({ item }: { item: Item }) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const toast = useToasts((s) => s.push)
  const identityQ = useQuery({ queryKey: qk.identity, queryFn: api.identity })
  const me = identityQ.data?.handle ?? null
  const [blockOpen, setBlockOpen] = useState(false)
  const [unblockOpen, setUnblockOpen] = useState(false)
  const [dismissOpen, setDismissOpen] = useState(false)
  const [moveOpen, setMoveOpen] = useState(false)
  const [retypeOpen, setRetypeOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)

  const run = async (label: string, patch: Parameters<typeof patchItemSafe>[2]) => {
    const res = await patchItemSafe(qc, item.id, patch)
    if (!res.ok) {
      toast('error', `${label} failed: ${res.error?.problem?.detail ?? res.error?.message ?? ''}`)
      return
    }
    toast('success', `${label}: ${item.shortId}`)
  }

  const isBlocked = item.blocked
  const actions = stateActions(item.state)
  const startableState = actions.startable
  // Start keeps an existing assignee; only an unassigned item needs "me".
  const needsHandle = !item.assignee && !me
  const canStart = startableState && !needsHandle
  const canDone = actions.done
  const canReopen = actions.reopen
  const canAssign = !!me && item.assignee !== me
  const canDismiss = actions.dismiss

  const cls = (active: boolean) =>
    'inline-flex items-center gap-1 text-xs px-2 py-1 rounded border ' +
    (active
      ? 'border-zinc-300 dark:border-zinc-700 text-zinc-700 dark:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-900'
      : 'border-zinc-200 dark:border-zinc-800 text-zinc-400 dark:text-zinc-600 cursor-not-allowed')
  const icon = 'h-3.5 w-3.5'

  const onUnblock = () => {
    if (impedimentCount(item) > 1) setUnblockOpen(true)
    else void directUnblock(item, qc, toast)
  }

  return (
    <div className="flex flex-wrap gap-1.5 min-w-0">
      {actions.approve && (
        <button
          type="button"
          title="Approve — promote this draft to todo so it can be picked up"
          onClick={() => void run('Approved', { state: 'todo' })}
          className={cls(true)}
        >
          <Stamp className={icon} aria-hidden />
          Approve
        </button>
      )}
      <button
        type="button"
        disabled={!canStart}
        title={
          startableState && needsHandle
            ? 'No identity configured — set one with knit config set --global handle <you>'
            : actions.sendBack
              ? 'Send back to in-progress — put the findings in a comment'
              : 'Set state to in-progress and assign to me'
        }
        onClick={() => {
          const patch: { state: string; assignee?: string } = { state: 'in-progress' }
          if (!item.assignee && me) patch.assignee = me
          void run(actions.sendBack ? 'Sent back' : 'Started', patch)
        }}
        className={cls(canStart)}
      >
        {actions.sendBack ? <Undo2 className={icon} aria-hidden /> : <Play className={icon} aria-hidden />}
        {actions.sendBack ? 'Send back' : 'Start'}
      </button>
      {actions.submit && (
        <button
          type="button"
          title="Submit for review — implemented, a final review pending"
          onClick={() => void run('Submitted', { state: 'review' })}
          className={cls(true)}
        >
          <Glasses className={icon} aria-hidden />
          Submit for review
        </button>
      )}
      <button
        type="button"
        disabled={!canDone}
        title="Mark as done"
        onClick={() => void run('Done', { state: 'done' })}
        className={cls(canDone)}
      >
        <CheckCircle2 className={icon} aria-hidden />
        Done
      </button>
      <button
        type="button"
        disabled={!canReopen}
        title="Reopen — set state back to todo"
        onClick={() => void run('Reopened', { state: 'todo' })}
        className={cls(canReopen)}
      >
        <RotateCcw className={icon} aria-hidden />
        Reopen
      </button>
      <button
        type="button"
        title={isBlocked ? 'Add another blocker or a reason' : 'Mark as blocked'}
        onClick={() => setBlockOpen(true)}
        className={cls(true)}
      >
        <Ban className={icon} aria-hidden />
        Block…
      </button>
      {isBlocked && (
        <button
          type="button"
          title="Resolve the blockers and the reason"
          onClick={onUnblock}
          className={cls(true)}
        >
          <CircleDot className={icon} aria-hidden />
          Unblock
        </button>
      )}
      <button
        type="button"
        disabled={!canAssign}
        title={me ? `Assign to ${me}` : 'No identity configured'}
        onClick={() => void run('Assigned', { assignee: me })}
        className={cls(canAssign)}
      >
        <UserPlus className={icon} aria-hidden />
        Assign to me
      </button>
      <MoreMenu
        canDismiss={canDismiss}
        onDismiss={() => setDismissOpen(true)}
        onMove={() => setMoveOpen(true)}
        onRetype={() => setRetypeOpen(true)}
        onDelete={() => setDeleteOpen(true)}
      />
      {blockOpen && (
        <BlockDialog item={item} onClose={() => setBlockOpen(false)} />
      )}
      {unblockOpen && (
        <UnblockDialog item={item} onClose={() => setUnblockOpen(false)} />
      )}
      {dismissOpen && (
        <DismissDialog item={item} onClose={() => setDismissOpen(false)} />
      )}
      {moveOpen && (
        <MoveParentDialog item={item} onClose={() => setMoveOpen(false)} />
      )}
      {retypeOpen && (
        <RetypeDialog item={item} onClose={() => setRetypeOpen(false)} />
      )}
      {deleteOpen && (
        <ItemDeleteDialog
          item={item}
          onClose={() => setDeleteOpen(false)}
          onDeleted={() =>
            void navigate({
              to: '.',
              search: (prev: Record<string, unknown>) => ({ ...prev, id: undefined }),
              replace: true,
            })
          }
        />
      )}
    </div>
  )
}

function MoreMenu({
  canDismiss,
  onDismiss,
  onMove,
  onRetype,
  onDelete,
}: {
  canDismiss: boolean
  onDismiss: () => void
  onMove: () => void
  onRetype: () => void
  onDelete: () => void
}) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  const itemClass =
    'flex items-center gap-2 w-full px-2 py-1.5 text-xs text-left rounded ' +
    'text-zinc-700 dark:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-800 ' +
    'disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent'
  const icon = 'h-3.5 w-3.5'
  const run = (cb: () => void) => () => {
    setOpen(false)
    cb()
  }

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        aria-label="More actions"
        aria-haspopup="menu"
        aria-expanded={open}
        title="More actions"
        onClick={() => setOpen((v) => !v)}
        className="inline-flex items-center gap-1 text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 text-zinc-700 dark:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-900"
      >
        <MoreHorizontal className={icon} aria-hidden />
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 mt-1 min-w-[12rem] rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-md p-1 z-20"
        >
          <button
            type="button"
            role="menuitem"
            disabled={!canDismiss}
            onClick={run(onDismiss)}
            className={itemClass}
            title="Dismiss this item without completing it"
          >
            <XCircle className={icon} aria-hidden />
            Dismiss…
          </button>
          <button
            type="button"
            role="menuitem"
            onClick={run(onMove)}
            className={itemClass}
            title="Move under a different parent"
          >
            <Move className={icon} aria-hidden />
            Move…
          </button>
          <button
            type="button"
            role="menuitem"
            onClick={run(onRetype)}
            className={itemClass}
            title="Change the item's type"
          >
            <ArrowRightLeft className={icon} aria-hidden />
            Change type…
          </button>
          <div className="my-1 border-t border-zinc-200 dark:border-zinc-800" />
          <button
            type="button"
            role="menuitem"
            onClick={run(onDelete)}
            className={
              'flex items-center gap-2 w-full px-2 py-1.5 text-xs text-left rounded ' +
              'text-red-700 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-950/40'
            }
            title="Permanently remove this item from disk (use Dismiss for soft-delete)"
          >
            <Trash2 className={icon} aria-hidden />
            Delete…
          </button>
        </div>
      )}
    </div>
  )
}

const blockedByLinks = (item: Item) => item.links.filter((l) => l.rel === 'Blocked by')

// Blocked ⇔ a free-text reason or a Blocked-by link — each is one impediment.
const impedimentCount = (item: Item) => blockedByLinks(item).length + (item.blockedReason ? 1 : 0)

const LinkSourceIcon = () => (
  <Link2 className="inline h-3 w-3 mr-0.5 -mt-0.5 shrink-0" aria-label="from a Blocked-by link">
    <title>From a Blocked-by link</title>
  </Link2>
)

function BlockedSummary({
  item,
  onEditLink,
  onSelectItem,
}: {
  item: Item
  onEditLink: (l: LinkRow) => void
  onSelectItem: (id: string) => void
}) {
  const links = blockedByLinks(item)
  const held = item.heldBy
  return (
    <>
      {held && (
        <span>
          blocked by parent{' '}
          <RowLink
            to="/items"
            search={{ id: held.id }}
            onSelect={() => onSelectItem(held.id)}
            className="cursor-pointer hover:underline"
          >
            {held.title}
          </RowLink>
          {item.blocked && '; '}
        </span>
      )}
      {item.blockedReason}
      {links.map((l, i) => (
        <span key={l.targetPath}>
          {(item.blockedReason || i > 0) && '; '}
          <button
            type="button"
            onClick={() => onEditLink(l)}
            title="From a Blocked-by link — edit it"
            aria-label={`Edit the Blocked-by link to ${l.targetText}`}
            className="rounded hover:bg-red-100 dark:hover:bg-red-900/40"
          >
            <Link2 className="inline h-3 w-3 mr-0.5 -mt-0.5" aria-hidden />
          </button>
          blocked by{' '}
          {l.targetItemId ? (
            <RowLink
              to="/items"
              search={{ id: l.targetItemId }}
              onSelect={() => onSelectItem(l.targetItemId!)}
              className="cursor-pointer hover:underline"
            >
              {l.targetText}
            </RowLink>
          ) : (
            l.targetText
          )}
          {l.notes && ` — ${l.notes}`}
        </span>
      ))}
    </>
  )
}

// Resolves every impediment in one call — the path for a lone one.
async function directUnblock(
  item: Item,
  qc: ReturnType<typeof useQueryClient>,
  toast: ReturnType<typeof useToasts.getState>['push'],
) {
  try {
    const fetched = await api.fetchItemWithEtag(item.id)
    const res = await api.unblockItem(item.id, fetched.etag)
    qc.setQueryData(qk.item(item.id), res.item)
    qc.invalidateQueries({ queryKey: qk.items })
    qc.invalidateQueries({ queryKey: qk.tree })
    toast('success', `Unblocked: ${item.shortId}`)
  } catch (e) {
    const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Unblock failed'
    toast('error', `Unblock failed: ${msg}`)
  }
}

function BlockedStripe({ item, onSelectItem }: { item: Item; onSelectItem: (id: string) => void }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [unblockOpen, setUnblockOpen] = useState(false)
  const [editOpen, setEditOpen] = useState(false)
  const [addOpen, setAddOpen] = useState(false)
  const [editLink, setEditLink] = useState<LinkRow | null>(null)
  const onClear = () => {
    if (impedimentCount(item) > 1) setUnblockOpen(true)
    else void directUnblock(item, qc, toast)
  }
  return (
    <div className="flex items-center gap-2 text-xs px-2 py-1.5 rounded border border-red-300 dark:border-red-900 bg-red-50 dark:bg-red-950/30 text-red-800 dark:text-red-200">
      <Ban className="h-3.5 w-3.5 shrink-0" />
      <span className="flex-1 min-w-0 break-words">
        <BlockedSummary item={item} onEditLink={setEditLink} onSelectItem={onSelectItem} />
      </span>
      {editLink && <EditLinkDialog item={item} link={editLink} onClose={() => setEditLink(null)} />}
      <button
        type="button"
        onClick={() => setAddOpen(true)}
        title="Add a blocker or a reason"
        aria-label="Add a blocker or a reason"
        className="p-0.5 rounded hover:bg-red-100 dark:hover:bg-red-900/40"
      >
        <Plus className="h-3 w-3" aria-hidden />
      </button>
      {addOpen && <BlockDialog item={item} onClose={() => setAddOpen(false)} />}
      {item.blockedReason && (
        <button
          type="button"
          onClick={() => setEditOpen(true)}
          title="Edit the reason"
          aria-label="Edit the reason"
          className="p-0.5 rounded hover:bg-red-100 dark:hover:bg-red-900/40"
        >
          <Pencil className="h-3 w-3" aria-hidden />
        </button>
      )}
      {editOpen && <EditReasonDialog item={item} onClose={() => setEditOpen(false)} />}
      {item.blocked && (
        <button
          type="button"
          onClick={onClear}
          title="Resolve the blockers and the reason"
          aria-label="Resolve the blockers and the reason"
          className="text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded hover:bg-red-100 dark:hover:bg-red-900/40"
        >
          Clear
        </button>
      )}
      {unblockOpen && <UnblockDialog item={item} onClose={() => setUnblockOpen(false)} />}
    </div>
  )
}

const REASON_KEY = '\u0000reason'

function useEscapeToClose(onClose: () => void) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])
}

// Resolve blocking per impediment: a partial selection removes the chosen
// links (and the reason, if picked) and the item stays blocked.
function UnblockDialog({ item, onClose }: { item: Item; onClose: () => void }) {
  useEscapeToClose(onClose)
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const links = blockedByLinks(item)
  const removable = links.filter((l) => l.targetItemId !== null)
  const total = impedimentCount(item)
  const [selected, setSelected] = useState<Set<string>>(
    () => new Set([...(item.blockedReason ? [REASON_KEY] : []), ...removable.map((l) => l.targetPath)]),
  )
  const [busy, setBusy] = useState(false)

  const toggle = (key: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const allSelected = selected.size === total
  const remaining = total - selected.size

  const submit = async () => {
    setBusy(true)
    try {
      if (allSelected) {
        const fetched = await api.fetchItemWithEtag(item.id)
        const res = await api.unblockItem(item.id, fetched.etag)
        qc.setQueryData(qk.item(item.id), res.item)
        toast('success', `Unblocked: ${item.shortId}`)
      } else {
        let etag = (await api.fetchItemWithEtag(item.id)).etag
        for (const l of removable) {
          if (!selected.has(l.targetPath)) continue
          const res = await api.deleteLink(item.id, 'blocked-by', l.targetItemId!, etag)
          qc.setQueryData(qk.item(item.id), res.item)
          etag = res.etag ?? (await api.fetchItemWithEtag(item.id)).etag
        }
        if (selected.has(REASON_KEY)) {
          const res = await api.patchItem(item.id, { blocked: '' }, etag)
          qc.setQueryData(qk.item(item.id), res.item)
        }
        toast('success', `Resolved ${selected.size} of ${total} — still blocked`)
      }
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      onClose()
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Unblock failed'
      toast('error', `Unblock failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  const rowClass = (disabled: boolean) =>
    'flex items-center gap-2 text-xs rounded border border-zinc-200 dark:border-zinc-800 px-2 py-1.5 ' +
    (disabled ? 'opacity-60' : 'cursor-pointer')

  return (
    <div
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold">Unblock {item.shortId}</h3>
        <div className="space-y-1">
          <div className="text-xs text-zinc-600 dark:text-zinc-300">
            What is resolved? Unselected ones stay, and the item remains blocked.
          </div>
          {item.blockedReason && (
            <label className={rowClass(false)}>
              <input
                type="checkbox"
                checked={selected.has(REASON_KEY)}
                onChange={() => toggle(REASON_KEY)}
              />
              <span className="min-w-0 break-words">Reason: {item.blockedReason}</span>
            </label>
          )}
          {links.map((l) => {
            const unresolved = l.targetItemId === null
            return (
              <label
                key={l.targetPath}
                className={rowClass(unresolved)}
                title={unresolved ? 'Unresolved link target — repair it via the Doctor first' : undefined}
              >
                <input
                  type="checkbox"
                  disabled={unresolved}
                  checked={selected.has(l.targetPath)}
                  onChange={() => toggle(l.targetPath)}
                />
                <span className="min-w-0 break-words">
                  <LinkSourceIcon />
                  Blocked by {l.targetText}
                  {l.notes && ` — ${l.notes}`}
                </span>
              </label>
            )
          })}
        </div>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy || selected.size === 0}
            onClick={() => void submit()}
            className="text-xs px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
          >
            {allSelected
              ? 'Unblock'
              : `Resolve ${selected.size} — still blocked (${remaining} left)`}
          </button>
        </div>
      </div>
    </div>
  )
}

// Edits only the free-text reason; blockers are added via Block… and
// resolved via Clear / Unblock.
function EditReasonDialog({ item, onClose }: { item: Item; onClose: () => void }) {
  useEscapeToClose(onClose)
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [reason, setReason] = useState(item.blockedReason ?? '')
  const [busy, setBusy] = useState(false)
  const save = async (value: string) => {
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.patchItem(item.id, { blocked: value }, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      toast('success', `${value ? 'Reason saved' : 'Reason cleared'}: ${item.shortId}`)
      onClose()
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Save failed'
      toast('error', `Save failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }
  const trimmed = reason.trim()
  return (
    <div
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900 p-4 space-y-3 text-zinc-900 dark:text-zinc-100"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold">Block reason of {item.shortId}</h3>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          aria-label="Block reason"
          placeholder="Why is this blocked?"
          rows={3}
          autoFocus
          className="w-full text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-950"
        />
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() => void save('')}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800 disabled:opacity-50"
          >
            Clear reason
          </button>
          <button
            type="button"
            disabled={busy || trimmed === item.blockedReason}
            onClick={() => void save(trimmed)}
            className="text-xs px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  )
}

function BlockDialog({ item, onClose }: { item: Item; onClose: () => void }) {
  useEscapeToClose(onClose)
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  // Adds an impediment. With a blocking item the text is that link's note (it
  // goes with the link); without one it is the free-text reason. Editing an
  // existing reason is the stripe's ✎.
  const [by, setBy] = useState<string | null>(null)
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const itemsQ = useQuery({ queryKey: qk.items, queryFn: () => api.items() })
  const byItem = by !== null
  const canSubmit = byItem || !!text.trim()
  const submit = async () => {
    if (!canSubmit) {
      toast('error', 'Give a reason or a blocking item')
      return
    }
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.blockItem(item.id, { by, reason: text.trim() || null }, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      toast('success', `Blocked: ${item.shortId}`)
      onClose()
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Block failed'
      toast('error', `Block failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }
  return (
    <div
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold">Block {item.shortId}</h3>
        <ItemPicker
          label="Blocking item (optional)"
          items={(itemsQ.data ?? []).filter((i) => i.id !== item.id)}
          value={by}
          onChange={setBy}
        />
        <label className="block text-xs">
          <span className="text-zinc-600 dark:text-zinc-300">
            {byItem
              ? 'Note on the link (optional — removed with the link)'
              : item.blockedReason
                ? 'Reason (replaces the current one — edit it with ✎ instead)'
                : 'Reason (an impediment that is not an item)'}
          </span>
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={byItem ? 'Why does that item block this one?' : 'Why is this blocked?'}
            rows={3}
            autoFocus
            className="mt-1 w-full px-2 py-1 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-950"
          />
        </label>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy || !canSubmit}
            onClick={() => void submit()}
            className="text-xs px-2 py-1 rounded bg-red-600 hover:bg-red-700 text-white disabled:opacity-50"
          >
            Block
          </button>
        </div>
      </div>
    </div>
  )
}

function DismissDialog({ item, onClose }: { item: Item; onClose: () => void }) {
  useEscapeToClose(onClose)
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  // Offer the cascade checkbox unconditionally — the server is the
  // source of truth and silently ignores it when no open children exist.
  const showDependents = useBlockedDependents((s) => s.showFor)
  const [cascade, setCascade] = useState(false)
  const [busy, setBusy] = useState(false)
  const submit = async () => {
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.dismissItem(item.id, cascade, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      const cascadedMsg = res.cascaded.length > 0 ? ` (+${res.cascaded.length} child)` : ''
      toast('success', `Dismissed: ${item.shortId}${cascadedMsg}`)
      onClose()
      showDependents([res.item, ...res.cascaded])
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Dismiss failed'
      toast('error', `Dismiss failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }
  return (
    <div
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold">Dismiss {item.shortId}</h3>
        <p className="text-xs text-zinc-600 dark:text-zinc-300">
          Mark this item as abandoned without completing it.
        </p>
        <label className="flex items-center gap-2 text-xs">
          <input
            type="checkbox"
            checked={cascade}
            onChange={(e) => setCascade(e.target.checked)}
          />
          Also dismiss open descendants
        </label>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() => void submit()}
            className="text-xs px-2 py-1 rounded bg-zinc-700 hover:bg-zinc-800 text-white disabled:opacity-50"
          >
            Dismiss
          </button>
        </div>
      </div>
    </div>
  )
}
