import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2 } from 'lucide-react'
import { ApiError } from '../api/client'
import {
  conventionsApi,
  conventionsQk,
  scopeView,
  SCOPE_LABEL,
  type ConventionsScope,
} from '../api/conventions'
import { BodyEditor } from './BodyEditor'
import { EditBodyButton } from './EditBodyButton'
import { MarkdownRender } from './MarkdownRender'
import { useToasts } from '../stores/toasts'

/**
 * The repo's and the user's rules for agents, the same two files
 * `knit conventions` prints. Repo first here: it is the one a reader of this
 * repo came for, and the one that wins a conflict.
 */
export function ConventionsTab() {
  const q = useQuery({ queryKey: conventionsQk.get(), queryFn: conventionsApi.get })
  const scopes = (['repo', 'user'] as const).flatMap((n) => q.data?.scopes.filter((s) => s.scope === n) ?? [])

  return (
    <div className="space-y-6">
      <p className="text-xs text-zinc-500">
        Rules agents read before working in this repo. The repo's win a conflict with your
        personal ones; both override the Knit skills' guidance, no other skill's. A rule a
        machine can check belongs in a template, a config key or a verify check instead.
      </p>
      {q.isLoading && <div className="text-sm text-zinc-500">Reading conventions…</div>}
      {q.error && (
        <div className="text-sm text-red-600 dark:text-red-400">Could not read conventions: {String(q.error)}</div>
      )}
      {scopes.map((s) => (
        <ScopeSection key={s.scope} scope={s} />
      ))}
    </div>
  )
}

function ScopeSection({ scope }: { scope: ConventionsScope }) {
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const view = scopeView(scope)

  const write = async (call: () => Promise<unknown>) => {
    setBusy(true)
    try {
      await call()
      setEditing(false)
      setConfirmDelete(false)
    } catch (e) {
      push(
        'error',
        e instanceof ApiError && e.status === 412
          ? 'The conventions changed elsewhere since you opened them — reloaded; edit again.'
          : `Could not write the conventions: ${String(e)}`,
      )
      if (e instanceof ApiError && e.status === 412) {
        setEditing(false)
        setConfirmDelete(false)
      }
    } finally {
      setBusy(false)
      void qc.invalidateQueries({ queryKey: conventionsQk.get() })
    }
  }
  const save = (text: string) => write(() => conventionsApi.put(scope.scope, text, scope.etag))
  const remove = () => write(() => conventionsApi.remove(scope.scope, scope.etag))

  return (
    <section className="space-y-1">
      <h2 className="text-xs font-medium text-zinc-500 uppercase tracking-wide">{SCOPE_LABEL[scope.scope]}</h2>
      <div className="text-[11px] font-mono text-zinc-500 break-all">
        {scope.path}
        {!view.canEdit && <span className="ml-2 font-sans">(read-only here — edit the file on its machine)</span>}
      </div>
      {editing ? (
        <BodyEditor initialValue={scope.text} onSave={save} onCancel={() => setEditing(false)} busy={busy} />
      ) : view.show === 'text' ? (
        <div
          className="group relative rounded-md border border-zinc-200 dark:border-zinc-800 p-3"
          onDoubleClick={view.canEdit ? () => setEditing(true) : undefined}
        >
          {view.canEdit && <EditBodyButton onClick={() => setEditing(true)} />}
          {view.canDelete && (
            // Beside the edit pen, sticking with it.
            <div className="sticky top-2 z-10 h-0">
              <button
                type="button"
                onClick={() => setConfirmDelete(true)}
                title="Delete these conventions"
                aria-label="Delete these conventions"
                className={
                  'absolute right-7 top-0 opacity-0 group-hover:opacity-100 focus:opacity-100 p-1 rounded text-zinc-500 ' +
                  'bg-white/90 dark:bg-zinc-950/90 hover:text-red-600 hover:bg-zinc-100 dark:hover:bg-zinc-900'
                }
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </div>
          )}
          <MarkdownRender body={scope.text} />
        </div>
      ) : (
        <div className="flex items-center gap-3 rounded-md border border-dashed border-zinc-300 dark:border-zinc-700 p-3 text-xs text-zinc-500">
          No conventions.
          {view.canEdit && (
            <button
              type="button"
              onClick={() => setEditing(true)}
              className="inline-flex items-center gap-1 rounded px-2 py-1 text-zinc-700 dark:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-800"
            >
              <Plus className="h-3.5 w-3.5" aria-hidden />
              Add conventions
            </button>
          )}
        </div>
      )}
      {confirmDelete && (
        <DeleteDialog scope={scope} busy={busy} onConfirm={() => void remove()} onCancel={() => setConfirmDelete(false)} />
      )}
    </section>
  )
}

interface DeleteDialogProps {
  scope: ConventionsScope
  busy: boolean
  onConfirm: () => void
  onCancel: () => void
}

function DeleteDialog({ scope, busy, onConfirm, onCancel }: DeleteDialogProps) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !busy) onCancel()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [busy, onCancel])

  return createPortal(
    <div
      className="fixed inset-0 z-50 bg-black/30 flex items-center justify-center p-4"
      onClick={busy ? undefined : onCancel}
    >
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="conventions-delete-title"
        className="w-full max-w-md rounded-md border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3
          id="conventions-delete-title"
          className="flex items-center gap-2 text-sm font-semibold text-zinc-800 dark:text-zinc-100"
        >
          <Trash2 className="h-4 w-4" aria-hidden />
          Delete {scope.scope === 'repo' ? "this repo's" : 'your personal'} conventions
        </h3>
        <p className="text-xs text-zinc-600 dark:text-zinc-300">
          Deletes <span className="font-mono break-all">{scope.path}</span>.{' '}
          {scope.scope === 'repo'
            ? 'Commit the deletion to drop the rules for everyone.'
            : 'Agents stop following these rules in every repo on this machine.'}
        </p>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            disabled={busy}
            onClick={onCancel}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800 hover:bg-zinc-100 dark:hover:bg-zinc-900 disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onConfirm}
            className="text-xs px-2 py-1 rounded bg-red-600 hover:bg-red-700 text-white disabled:opacity-50"
          >
            Delete
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
