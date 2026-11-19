import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, api, qk } from '../api/client'
import type { Item } from '../api/types'
import { useToasts } from '../stores/toasts'

interface Props {
  item: Item
  onClose: () => void
}

export function CommentComposer({ item, onClose }: Props) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [body, setBody] = useState('')
  const [busy, setBusy] = useState(false)
  const ref = useRef<HTMLTextAreaElement | null>(null)

  useEffect(() => {
    ref.current?.focus()
  }, [])

  const submit = async () => {
    const text = body.trim()
    if (!text) {
      toast('error', 'Comment body cannot be empty')
      return
    }
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.addComment(item.id, { body: text }, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.result.item)
      toast('success', 'Comment added')
      onClose()
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Add failed'
      toast('error', `Add failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 p-3 space-y-2">
      <textarea
        ref={ref}
        value={body}
        onChange={(e) => setBody(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            e.preventDefault()
            onClose()
          } else if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') {
            e.preventDefault()
            void submit()
          }
        }}
        placeholder="Write a comment… (Ctrl/Cmd-Enter to submit, Esc to cancel)"
        rows={3}
        className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-950 text-sm"
      />
      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
        >
          Cancel
        </button>
        <button
          type="button"
          disabled={busy || !body.trim()}
          onClick={() => void submit()}
          className="text-xs px-2 py-1 rounded bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {busy ? 'Posting…' : 'Done'}
        </button>
      </div>
    </div>
  )
}
