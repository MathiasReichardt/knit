import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { Flag, X } from 'lucide-react'
import { projectsApi } from '../api/projects'
import { slugify, isSlugTruncated } from '../lib/slug'
import { useDialogs } from '../stores/dialogs'
import { useToasts } from '../stores/toasts'

const STATES = ['planned', 'reached', 'dropped'] as const

export function NewMilestoneDialog() {
  const open = useDialogs((s) => s.newMilestoneOpen)
  const prefill = useDialogs((s) => s.newMilestonePrefill)
  const close = useDialogs((s) => s.closeNewMilestone)
  if (!open || !prefill) return null
  return <DialogBody project={prefill.project} onClose={close} />
}

function DialogBody({ project, onClose }: { project: string; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const navigate = useNavigate()
  const [title, setTitle] = useState('')
  const [slug, setSlug] = useState('')
  const [target, setTarget] = useState('')
  const [state, setState] = useState<(typeof STATES)[number]>('planned')
  const titleRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => { titleRef.current?.focus() }, [])
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // The server slugifies the slug (lowercase / dash / truncate to 40). Preview
  // the stored form: derived from the title when the slug field is empty, else
  // the normalized typed slug when it differs from what was typed.
  const derivedSlug = title.trim() ? slugify(title) : ''
  const showDerivedHint = slug.trim().length === 0 && derivedSlug.length > 0
  const derivedTruncated = derivedSlug.length > 0 && isSlugTruncated(title)
  const normalizedSlug = slug.trim() ? slugify(slug) : ''
  const showNormalizedPreview = normalizedSlug.length > 0 && normalizedSlug !== slug.trim()

  // Slug required — the create endpoint needs it. When blank we send the
  // title-derived slug so a user can just type a title.
  const effectiveSlug = slug.trim() || derivedSlug
  const canSubmit = effectiveSlug.length > 0 && !title.trim().startsWith(' ')

  const mutation = useMutation({
    mutationFn: () =>
      projectsApi.createMilestone(project, {
        slug: effectiveSlug,
        title: title.trim() || undefined,
        target: target.trim() || undefined,
        state,
      }),
    onSuccess: (m) => {
      toast('success', `Created milestone ${m.project}/${m.slug}`)
      void qc.invalidateQueries({ queryKey: ['projects'] })
      onClose()
      void navigate({
        to: '/projects',
        search: { mp: m.project, ms: m.slug } as never,
      }).catch(() => {})
    },
    onError: (e) => toast('error', `Create failed: ${String(e)}`),
  })

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div
        role="dialog"
        aria-modal
        aria-label="New milestone"
        className="w-[34rem] max-w-[95vw] max-h-[90vh] overflow-y-auto rounded-md bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 shadow-xl"
      >
        <div className="flex items-center px-4 py-2 border-b border-zinc-200 dark:border-zinc-800">
          <Flag className="h-4 w-4 text-cyan-600 dark:text-cyan-400 mr-2" aria-hidden />
          <h2 className="text-sm font-semibold text-zinc-800 dark:text-zinc-100">New milestone</h2>
          <button
            type="button"
            onClick={onClose}
            className="ml-auto p-1 rounded text-zinc-500 hover:bg-zinc-200 dark:hover:bg-zinc-800"
            aria-label="Close"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="px-4 py-3 space-y-3 text-sm">
          <div className="rounded border border-blue-200 dark:border-blue-900 bg-blue-50 dark:bg-blue-950/40 px-3 py-2 text-xs text-blue-900 dark:text-blue-100">
            Under project <span className="font-mono font-medium">{project}</span>
          </div>

          <div>
            <label className="block text-xs text-zinc-500 mb-1">Title</label>
            <input
              ref={titleRef}
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Beta cut"
              className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
            />
          </div>

          <div>
            <label className="block text-xs text-zinc-500 mb-1">
              Slug <span className="text-red-500">*</span>
            </label>
            <input
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              placeholder={derivedSlug || 'beta'}
              className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm font-mono"
            />
            {showDerivedHint ? (
              <p className="mt-1 text-xs text-zinc-400">
                {derivedTruncated ? 'derived from title (shortened to fit)' : 'derived from title'}
              </p>
            ) : showNormalizedPreview ? (
              <p className="mt-1 text-xs text-zinc-400">
                will be stored as <span className="font-mono text-zinc-500">{normalizedSlug}</span>
              </p>
            ) : null}
          </div>

          <div className="flex gap-3">
            <div className="flex-1">
              <label className="block text-xs text-zinc-500 mb-1">Target (optional)</label>
              <input
                type="date"
                value={target}
                onChange={(e) => setTarget(e.target.value)}
                className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
              />
            </div>
            <div className="flex-1">
              <label className="block text-xs text-zinc-500 mb-1">State</label>
              <select
                value={state}
                onChange={(e) => setState(e.target.value as (typeof STATES)[number])}
                className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
              >
                {STATES.map((s) => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2 px-4 py-2 border-t border-zinc-200 dark:border-zinc-800">
          <button
            type="button"
            onClick={onClose}
            className="px-3 py-1 rounded text-sm text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={!canSubmit || mutation.isPending}
            onClick={() => mutation.mutate()}
            className="ml-auto px-3 py-1 rounded bg-blue-600 text-white text-sm disabled:opacity-50"
          >
            {mutation.isPending ? 'Creating…' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  )
}
