import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { FileText, X } from 'lucide-react'
import { projectsApi } from '../api/projects'
import { slugify, isSlugTruncated } from '../lib/slug'
import { useDialogs } from '../stores/dialogs'
import { useToasts } from '../stores/toasts'

// Create a free-form project doc — mirrors NewKbDialog (title/slug + a
// createAsFolder checkbox). New .md files and sub-folders land under
// .knit/projects/<slug>/, browsable + searchable, inert to the link machinery.
export function NewProjectDocDialog() {
  const open = useDialogs((s) => s.newProjectDocOpen)
  const prefill = useDialogs((s) => s.newProjectDocPrefill)
  const close = useDialogs((s) => s.closeNewProjectDoc)
  if (!open || !prefill) return null
  return <DialogBody project={prefill.project} onClose={close} />
}

function DialogBody({ project, onClose }: { project: string; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const navigate = useNavigate()
  const [title, setTitle] = useState('')
  const [slug, setSlug] = useState('')
  const [asFolder, setAsFolder] = useState(false)
  const titleRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => { titleRef.current?.focus() }, [])
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // The doc slug is derived from the title when blank; a typed slug is
  // reslugified server-side (lowercase / dash / truncate), so preview the
  // stored form when it differs.
  const derivedSlug = title.trim() ? slugify(title) : ''
  const showDerivedHint = slug.trim().length === 0 && derivedSlug.length > 0
  const derivedTruncated = derivedSlug.length > 0 && isSlugTruncated(title)
  const normalizedSlug = slug.trim() ? slugify(slug) : ''
  const showNormalizedPreview = normalizedSlug.length > 0 && normalizedSlug !== slug.trim()

  const effectiveSlug = slug.trim() || derivedSlug
  const canSubmit = effectiveSlug.length > 0

  const mutation = useMutation({
    mutationFn: () =>
      projectsApi.createDoc(project, {
        slug: slug.trim() || undefined,
        title: title.trim() || undefined,
        asFolder,
      }),
    onSuccess: (doc) => {
      toast('success', `Created ${doc.project}/${doc.path}`)
      void qc.invalidateQueries({ queryKey: ['projects'] })
      onClose()
      void navigate({
        to: '/projects',
        search: { doc: `${doc.project}/${doc.path}` } as never,
      }).catch(() => {})
    },
    onError: (e) => toast('error', `Create failed: ${String(e)}`),
  })

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div
        role="dialog"
        aria-modal
        aria-label="New document"
        className="w-[34rem] max-w-[95vw] max-h-[90vh] overflow-y-auto rounded-md bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 shadow-xl"
      >
        <div className="flex items-center px-4 py-2 border-b border-zinc-200 dark:border-zinc-800">
          <FileText className="h-4 w-4 text-zinc-500 mr-2" aria-hidden />
          <h2 className="text-sm font-semibold text-zinc-800 dark:text-zinc-100">New document</h2>
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
              placeholder="Launch notes"
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
              placeholder={derivedSlug || 'notes'}
              className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm font-mono"
            />
            {showDerivedHint ? (
              <p className="mt-1 text-xs text-zinc-400">
                {derivedTruncated ? 'derived from title (shortened to fit)' : 'derived from title'}
              </p>
            ) : showNormalizedPreview ? (
              <p className="mt-1 text-xs text-zinc-400">
                will be stored as <span className="font-mono text-zinc-500">{normalizedSlug}.md</span>
              </p>
            ) : null}
          </div>

          <label className="flex items-center gap-2 text-xs text-zinc-600 dark:text-zinc-400 select-none">
            <input
              type="checkbox"
              checked={asFolder}
              onChange={(e) => setAsFolder(e.target.checked)}
              className="h-3.5 w-3.5"
            />
            <span>Create as folder form (<span className="font-mono">{effectiveSlug || 'slug'}/readme.md</span>, allows sub-docs)</span>
          </label>
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
