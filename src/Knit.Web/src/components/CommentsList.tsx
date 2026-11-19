import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import { ApiError, api, qk } from '../api/client'
import type { Comment, Item } from '../api/types'
import { useToasts } from '../stores/toasts'

interface Props {
  item: Item
}

export function CommentsList({ item }: Props) {
  if (!item.comments.length) {
    return <p className="text-sm italic text-zinc-500">No comments.</p>
  }
  return (
    <ul className="space-y-3">
      {item.comments.map((c) => (
        <CommentRow key={c.id} item={item} comment={c} />
      ))}
    </ul>
  )
}

function CommentRow({ item, comment }: { item: Item; comment: Comment }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [mode, setMode] = useState<'view' | 'edit' | 'confirm-delete'>('view')
  const [draft, setDraft] = useState(comment.body)
  const [busy, setBusy] = useState(false)

  const onSave = async () => {
    const body = draft.trim()
    if (!body) {
      toast('error', 'Comment body cannot be empty')
      return
    }
    if (body === comment.body) {
      setMode('view')
      return
    }
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.patchComment(item.id, comment.id, body, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.item)
      toast('success', 'Comment updated')
      setMode('view')
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Update failed'
      toast('error', `Update failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  const onDelete = async () => {
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.deleteComment(item.id, comment.id, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.item)
      toast('success', 'Comment deleted')
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Delete failed'
      toast('error', `Delete failed: ${msg}`)
    } finally {
      setBusy(false)
      setMode('view')
    }
  }

  return (
    <li className="group rounded-md border border-zinc-200 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-900/50 px-3 py-2">
      <div className="flex items-center justify-between text-xs text-zinc-500 mb-1">
        <span className="font-medium text-zinc-700 dark:text-zinc-300">{comment.author}</span>
        <div className="flex items-center gap-2">
          <time dateTime={comment.created} title={new Date(comment.created).toISOString()}>
            {formatDate(comment.created)}
          </time>
          {mode === 'view' && (
            <span className="opacity-0 group-hover:opacity-100 focus-within:opacity-100 inline-flex items-center gap-0.5">
              <button
                type="button"
                onClick={() => {
                  setDraft(comment.body)
                  setMode('edit')
                }}
                title="Edit"
                aria-label="Edit comment"
                className="p-0.5 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800"
              >
                <Pencil className="h-3 w-3" />
              </button>
              <button
                type="button"
                onClick={() => setMode('confirm-delete')}
                title="Delete"
                aria-label="Delete comment"
                className="p-0.5 rounded text-zinc-500 hover:text-red-600 hover:bg-zinc-200 dark:hover:bg-zinc-800"
              >
                <Trash2 className="h-3 w-3" />
              </button>
            </span>
          )}
        </div>
      </div>
      {mode === 'edit' ? (
        <div className="space-y-2">
          <textarea
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            autoFocus
            rows={Math.max(3, draft.split('\n').length)}
            className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-950 text-sm"
          />
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => {
                setDraft(comment.body)
                setMode('view')
              }}
              className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => void onSave()}
              className="text-xs px-2 py-1 rounded bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {busy ? 'Saving…' : 'Save'}
            </button>
          </div>
        </div>
      ) : mode === 'confirm-delete' ? (
        <div className="space-y-2">
          <p className="text-sm text-zinc-700 dark:text-zinc-300">
            Delete this comment? Cannot be undone.
          </p>
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setMode('view')}
              className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => void onDelete()}
              className="text-xs px-2 py-1 rounded bg-red-600 text-white hover:bg-red-700 disabled:opacity-50"
            >
              Delete
            </button>
          </div>
        </div>
      ) : (
        <p className="text-sm whitespace-pre-wrap text-zinc-800 dark:text-zinc-200">{comment.body}</p>
      )}
    </li>
  )
}

function formatDate(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
  } catch {
    return iso
  }
}
