import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/client'
import type { EditPlace, Landing } from './editPlace'

/**
 * Resource-specific glue for {@link useBodyEditorSession}. Each editor
 * (item body, KB entry, design doc) supplies how to read/write its doc;
 * the hook owns the conflict lifecycle so all three behave identically.
 */
export interface BodyEditorPort {
  /**
   * Read the current body **and its concurrency etag** at the moment the
   * editor opens. The etag is frozen for the whole edit session, so a save
   * that lands after another writer touched the doc fails the If-Match and
   * surfaces a conflict instead of silently clobbering.
   */
  fetchWithEtag: () => Promise<{ body: string; etag: string }>
  /** Write `body` with the given `If-Match` value. `'*'` force-writes. */
  patch: (body: string, ifMatch: string) => Promise<void>
  /** Invalidate dependent caches after a successful write. */
  onSaved: () => void
  /** Invalidate the entry query so "Reload" reseeds from the server. */
  onReload: () => void
  toast: (kind: 'success' | 'error' | 'info', message: string) => void
  /**
   * The body of the doc as last fetched by the host's query — kept fresh by
   * the SSE-driven invalidation. The hook watches this to notice the doc
   * moving under an **open** editor: when it diverges from the base
   * the editor opened on, a clean editor silently re-seeds and a dirty one
   * raises a non-blocking "changed on disk" banner.
   */
  currentBody: string
}

export interface BodyEditorSession {
  editing: boolean
  busy: boolean
  /** Body the editor must seed from — the version fetched when it opened. */
  seedBody: string | null
  /** True while the 412 conflict dialog should be shown. */
  conflictActive: boolean
  /** True while the non-blocking "changed on disk" banner should be shown. */
  staleActive: boolean
  /** Bumped to push a fresh doc into a passive editor (clean re-seed). */
  reseedToken: number
  /** The editor reports its dirty flag up so the hook can pick re-seed vs warn. */
  setEditorDirty: (dirty: boolean) => void
  /** Enter edit mode (async: captures the body + etag first), at `place` when given. */
  open: (place?: EditPlace) => void
  /** Where the editor opens: the double-clicked spot, or null for the top. */
  openPlace: EditPlace | null
  /** Leave the editor; the view then lands on `cursor`. */
  cancel: (cursor?: EditPlace) => void
  save: (next: string, cursor?: EditPlace) => Promise<void>
  /** Where the view lands after the editor closed; cleared by `clearLanding`. */
  landing: Landing | null
  clearLanding: () => void
  /** Replay the conflicted save, forcing past the stale etag. */
  overwrite: () => Promise<void>
  /** Discard local edits and reseed from the server (conflict dialog). */
  reload: () => void
  /** Dismiss the conflict dialog but stay in the editor. */
  keepEditing: () => void
  /** Banner "Reload": adopt the remote revision into the open editor. */
  reloadStale: () => Promise<void>
  /** Banner "Keep mine": dismiss the banner, leaving the save to 412 later. */
  dismissStale: () => void
  /**
   * Write a derived body edit (checkbox toggle, diagram layout) in one shot,
   * outside the editor: there's no open edit to protect, so it takes the fresh etag.
   */
  quickSave: (next: string) => Promise<void>
}

export function useBodyEditorSession(port: BodyEditorPort): BodyEditorSession {
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [seedBody, setSeedBody] = useState<string | null>(null)
  // The etag of the version the editor opened on, frozen for the session.
  const [editEtag, setEditEtag] = useState('')
  // Body captured when Save was pressed — replayed verbatim on Overwrite so
  // the user keeps what they tried to save, not whatever they typed since.
  const [conflictBuffer, setConflictBuffer] = useState<string | null>(null)
  const [staleActive, setStaleActive] = useState(false)
  const [reseedToken, setReseedToken] = useState(0)
  const [openPlace, setOpenPlace] = useState<EditPlace | null>(null)
  const [landing, setLanding] = useState<Landing | null>(null)

  // The server body our editor is currently based on. A `currentBody` that
  // diverges from this means the doc moved underneath us.
  const baseBodyRef = useRef<string | null>(null)
  // Dirty is owned by the editor; mirror it into a ref so the staleness
  // effect can read it without re-subscribing.
  const dirtyRef = useRef(false)

  const open = (place?: EditPlace) => {
    void (async () => {
      try {
        const { body, etag } = await port.fetchWithEtag()
        setOpenPlace(place ?? null)
        setLanding(null)
        setSeedBody(body)
        setEditEtag(etag)
        baseBodyRef.current = body
        dirtyRef.current = false
        setStaleActive(false)
        setEditing(true)
      } catch (e) {
        const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : String(e)
        port.toast('error', `Could not open editor: ${msg}`)
      }
    })()
  }

  // Pull the freshest revision into the editor and re-base on it (etag + body).
  // Used both for a passive clean re-seed and for the banner's "Reload".
  const adoptLatest = async () => {
    try {
      const { body, etag } = await port.fetchWithEtag()
      baseBodyRef.current = body
      setSeedBody(body)
      setEditEtag(etag)
      dirtyRef.current = false
      setStaleActive(false)
      setReseedToken((t) => t + 1)
    } catch {
      // Leave the editor as-is; the next change re-triggers detection.
    }
  }

  // Watch the host query's body. When it diverges from our base while editing,
  // a clean editor silently adopts it; a dirty one raises the banner.
  useEffect(() => {
    if (!editing) return
    if (port.currentBody === baseBodyRef.current) return
    if (dirtyRef.current) {
      // Re-base detection on this revision so the banner doesn't re-fire for
      // it; the frozen etag stays stale on purpose so Save still 412s.
      baseBodyRef.current = port.currentBody
      setStaleActive(true)
    } else {
      void adoptLatest()
    }
    // adoptLatest closes over the port; deps intentionally minimal.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [port.currentBody, editing])

  const write = async (next: string, ifMatch: string): Promise<boolean> => {
    try {
      await port.patch(next, ifMatch)
      port.onSaved()
      return true
    } catch (e) {
      if (e instanceof ApiError && e.status === 412) {
        setConflictBuffer(next)
        return false
      }
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : String(e)
      port.toast('error', `Save failed: ${msg}`)
      return false
    }
  }

  const leaveEditor = () => {
    setEditing(false)
    setStaleActive(false)
  }

  const save = async (next: string, cursor?: EditPlace) => {
    // Unchanged from the version we opened on — close without a redundant write.
    if (next === seedBody) {
      cancel(cursor)
      return
    }
    setBusy(true)
    const ok = await write(next, editEtag)
    setBusy(false)
    if (ok) {
      port.toast('success', 'Saved')
      leaveEditor()
      setLanding(cursor ? { ...cursor, text: next } : null)
    }
  }

  const cancel = (cursor?: EditPlace) => {
    leaveEditor()
    setLanding(cursor ?? null)
  }

  const overwrite = async () => {
    if (conflictBuffer == null) return
    setBusy(true)
    try {
      await port.patch(conflictBuffer, '*')
      port.onSaved()
      port.toast('success', 'Overwrote remote changes')
      setConflictBuffer(null)
      leaveEditor()
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : String(e)
      port.toast('error', `Overwrite failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  const quickSave = async (next: string) => {
    try {
      const { etag } = await port.fetchWithEtag()
      await port.patch(next, etag)
      port.onSaved()
      port.toast('success', 'Saved')
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : String(e)
      port.toast('error', `Save failed: ${msg}`)
    }
  }

  const reload = () => {
    setConflictBuffer(null)
    leaveEditor()
    port.onReload()
  }

  return {
    editing,
    busy,
    seedBody,
    conflictActive: conflictBuffer != null,
    staleActive,
    reseedToken,
    setEditorDirty: (dirty) => {
      dirtyRef.current = dirty
    },
    open,
    openPlace,
    cancel,
    save,
    landing,
    clearLanding: () => setLanding(null),
    overwrite,
    reload,
    keepEditing: () => setConflictBuffer(null),
    reloadStale: adoptLatest,
    dismissStale: () => setStaleActive(false),
    quickSave,
  }
}
