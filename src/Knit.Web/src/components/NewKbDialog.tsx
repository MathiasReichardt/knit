import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { X, BookText, FileText, Star, ScrollText, Lightbulb, Link2 } from 'lucide-react'
import { allowsGrowToFolder, kbApi, type KbBucket } from '../api/kb'
import { slugify, isSlugTruncated } from '../lib/slug'
import { useDialogs } from '../stores/dialogs'
import { useToasts } from '../stores/toasts'

const BUCKETS: { kind: KbBucket; label: string; desc: string; Icon: typeof BookText }[] = [
  { kind: 'adr', label: 'ADR', desc: 'Architecture decision record', Icon: BookText },
  { kind: 'doc', label: 'Doc', desc: 'Living doc / cross-cutting reference', Icon: FileText },
  { kind: 'feature', label: 'Feature', desc: 'Feature spec & notes', Icon: Star },
  { kind: 'log', label: 'Log', desc: 'Dated log entry', Icon: ScrollText },
  { kind: 'idea', label: 'Idea', desc: 'Quick idea capture', Icon: Lightbulb },
  { kind: 'source', label: 'Source', desc: 'External source / link', Icon: Link2 },
]

export function NewKbDialog() {
  const open = useDialogs((s) => s.newKbOpen)
  const prefill = useDialogs((s) => s.newKbPrefill)
  const close = useDialogs((s) => s.closeNewKb)
  if (!open) return null
  const initialBucket = prefill.parentRef?.bucket ?? prefill.bucket ?? 'doc'
  return (
    <DialogBody
      prefillBucket={initialBucket}
      parentRef={prefill.parentRef}
      onClose={close}
    />
  )
}

function DialogBody({
  prefillBucket,
  parentRef,
  onClose,
}: {
  prefillBucket: KbBucket
  parentRef?: { bucket: KbBucket; slug: string }
  onClose: () => void
}) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const navigate = useNavigate()
  const [bucket, setBucket] = useState<KbBucket>(prefillBucket)
  const bucketLocked = parentRef !== undefined
  const slugPrefix = parentRef ? parentRef.slug + '/' : ''
  const [title, setTitle] = useState('')
  const [slug, setSlug] = useState('')
  const [summary, setSummary] = useState('')
  const [feature, setFeature] = useState('')
  const [sourceKind, setSourceKind] = useState<'link' | 'file'>('link')
  const [url, setUrl] = useState('')
  const [path, setPath] = useState('')
  const [createAsFolder, setCreateAsFolder] = useState(false)
  const titleRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => { titleRef.current?.focus() }, [bucket])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const mutation = useMutation({
    mutationFn: () =>
      kbApi.create({
        bucket,
        title: title.trim() || undefined,
        slug: slug.trim() ? slugPrefix + slug.trim() : (slugPrefix || undefined),
        summary: summary.trim() || undefined,
        feature: feature.trim() || undefined,
        sourceKind: bucket === 'source' ? sourceKind : undefined,
        url: bucket === 'source' && sourceKind === 'link' ? url.trim() : undefined,
        path: bucket === 'source' && sourceKind === 'file' ? path.trim() : undefined,
      }),
    onSuccess: async (entry) => {
      // Folder-form requested? Immediately split. The create has already
      // succeeded — if split fails, the entry stays in flat form and we
      // surface the partial failure rather than hiding it.
      const wantFolder = createAsFolder && allowsGrowToFolder(entry.bucket) && !parentRef
      if (wantFolder) {
        try {
          await kbApi.split(entry.ref)
          toast('success', `Created ${entry.ref} (folder form)`)
        } catch (e) {
          toast('error', `Created ${entry.ref} but split failed: ${String(e)}`)
        }
      } else {
        toast('success', `Created ${entry.ref}`)
      }
      qc.invalidateQueries({ queryKey: ['kb'] })
      onClose()
      void navigate({
        to: '/kb',
        search: { bucket: entry.bucket, slug: entry.slug } as never,
      }).catch(() => {})
    },
    onError: (e) => toast('error', `Create failed: ${String(e)}`),
  })

  const requiresTitle = bucket === 'adr' || bucket === 'log' || bucket === 'idea'
  // Feature requires an explicit slug only at top level; as a sub-doc the
  // leaf is derived from the title (or overridden by typing a slug).
  const requiresSlug = (bucket === 'feature' && !parentRef) || bucket === 'source'
  // A sub-doc needs at least a title (to derive the leaf) or an explicit
  // slug to name the new entry.
  const subdocNeedsName = parentRef !== undefined && allowsGrowToFolder(bucket)
  // Buckets whose slug the backend derives from the title when left blank.
  // (feature only when it's a sub-doc; top-level feature/source need an
  // explicit slug, adr/log carry a number/date prefix.)
  const derivesFromTitle = bucket === 'doc' || bucket === 'idea'
    || (bucket === 'feature' && parentRef !== undefined)
  // Live preview of the leaf the backend will generate, shown as the slug
  // input's placeholder while the field is empty so the user sees what they
  // will get. Typing a slug overrides it.
  const derivedSlug = derivesFromTitle && title.trim() ? slugify(title) : ''
  const showDerivedHint = derivesFromTitle && slug.trim().length === 0
  const derivedTruncated = derivedSlug.length > 0 && isSlugTruncated(title)
  // Every slug-bearing bucket (feature/doc/idea/source) reslugifies each
  // typed segment server-side (lowercase / dash / truncate to 40), so a
  // typed slug is rarely stored verbatim. Preview the stored form when it
  // differs so the transform isn't silent. (adr/log derive their slug from
  // the title and don't expose a slug field.)
  const slugifiesServerSide = bucket !== 'adr' && bucket !== 'log'
  const normalizedSlug = slugifiesServerSide && slug.trim()
    ? slug.trim().split('/').map(slugify).join('/')
    : ''
  const showNormalizedPreview = normalizedSlug.length > 0 && normalizedSlug !== slug.trim()
  const canSubmit =
    !mutation.isPending &&
    (!requiresTitle || title.trim().length > 0) &&
    (!requiresSlug || slug.trim().length > 0) &&
    (!subdocNeedsName || title.trim().length > 0 || slug.trim().length > 0) &&
    (bucket !== 'source' || (sourceKind === 'link' ? url.trim().length > 0 : path.trim().length > 0))

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div
        role="dialog"
        aria-modal
        aria-label="New KB entry"
        className="w-[40rem] max-w-[95vw] max-h-[90vh] overflow-y-auto rounded-md bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 shadow-xl"
      >
        <div className="flex items-center px-4 py-2 border-b border-zinc-200 dark:border-zinc-800">
          <h2 className="text-sm font-semibold text-zinc-800 dark:text-zinc-100">
            {parentRef ? 'New sub-doc' : 'New KB entry'}
          </h2>
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
          {parentRef && (
            <div className="rounded border border-blue-200 dark:border-blue-900 bg-blue-50 dark:bg-blue-950/40 px-3 py-2 text-xs text-blue-900 dark:text-blue-100">
              Sub-doc of <span className="font-mono font-medium">{parentRef.bucket}:{parentRef.slug}</span>
            </div>
          )}
          <div>
            <label className="block text-xs text-zinc-500 mb-1">Kind</label>
            <div className="grid grid-cols-3 gap-2">
              {BUCKETS.map((b) => {
                const active = b.kind === bucket
                const disabled = bucketLocked && b.kind !== bucket
                const Icon = b.Icon
                return (
                  <button
                    key={b.kind}
                    type="button"
                    onClick={() => { if (!bucketLocked) setBucket(b.kind) }}
                    disabled={disabled}
                    className={
                      'flex flex-col items-start gap-1 p-2 rounded border text-left ' +
                      (active
                        ? 'bg-blue-50 dark:bg-blue-950/40 border-blue-300 dark:border-blue-700 text-blue-900 dark:text-blue-100'
                        : 'border-zinc-200 dark:border-zinc-800 hover:bg-zinc-50 dark:hover:bg-zinc-800/60') +
                      (disabled ? ' opacity-40 cursor-not-allowed' : '')
                    }
                  >
                    <div className="flex items-center gap-1.5">
                      <Icon className="h-4 w-4" aria-hidden />
                      <span className="font-medium">{b.label}</span>
                    </div>
                    <div className="text-[11px] text-zinc-500">{b.desc}</div>
                  </button>
                )
              })}
            </div>
          </div>

          {(requiresSlug || bucket === 'doc' || bucket === 'idea'
            || (parentRef !== undefined && bucket === 'feature')) && (
            <div>
              <label className="block text-xs text-zinc-500 mb-1">
                {bucket === 'source' ? 'Id' : 'Slug'} {requiresSlug && <span className="text-red-500">*</span>}
              </label>
              <div className="flex items-stretch rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 overflow-hidden">
                {slugPrefix && (
                  <span className="px-2 py-1 text-sm font-mono text-zinc-500 bg-zinc-100 dark:bg-zinc-800 border-r border-zinc-300 dark:border-zinc-700 select-none">
                    {slugPrefix}
                  </span>
                )}
                <input
                  value={slug}
                  onChange={(e) => setSlug(e.target.value)}
                  placeholder={
                    derivedSlug ? derivedSlug :
                    slugPrefix ? '(derived from title)' :
                    bucket === 'source' ? 'rfc-9457' :
                    bucket === 'feature' ? 'user-mgmt' :
                    '(optional — derived from title)'
                  }
                  className="flex-1 px-2 py-1 text-sm bg-transparent outline-none placeholder:text-zinc-400"
                />
              </div>
              {showDerivedHint ? (
                <p className="mt-1 text-xs text-zinc-400">
                  {derivedTruncated ? 'derived from title (shortened to fit)' : 'derived from title'}
                </p>
              ) : showNormalizedPreview ? (
                <p className="mt-1 text-xs text-zinc-400">
                  will be stored as <span className="font-mono text-zinc-500 dark:text-zinc-400">{normalizedSlug}</span>
                </p>
              ) : null}
            </div>
          )}

          <div>
            <label className="block text-xs text-zinc-500 mb-1">
              Title {requiresTitle && <span className="text-red-500">*</span>}
            </label>
            <input
              ref={titleRef}
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
            />
          </div>

          {allowsGrowToFolder(bucket) && !parentRef && (
            <label className="flex items-center gap-2 text-xs text-zinc-600 dark:text-zinc-400 select-none">
              <input
                type="checkbox"
                checked={createAsFolder}
                onChange={(e) => setCreateAsFolder(e.target.checked)}
                className="h-3.5 w-3.5"
              />
              <span>Create as folder form (allow sub-docs)</span>
            </label>
          )}

          <div>
            <label className="block text-xs text-zinc-500 mb-1">Summary</label>
            <input
              value={summary}
              onChange={(e) => setSummary(e.target.value)}
              className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
            />
          </div>

          {bucket !== 'feature' && bucket !== 'source' && (
            <div>
              <label className="block text-xs text-zinc-500 mb-1">Feature (optional)</label>
              <input
                value={feature}
                onChange={(e) => setFeature(e.target.value)}
                className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
              />
            </div>
          )}

          {bucket === 'source' && (
            <>
              <div>
                <label className="block text-xs text-zinc-500 mb-1">Source kind</label>
                <div className="flex gap-2">
                  {(['link', 'file'] as const).map((k) => (
                    <button
                      key={k}
                      type="button"
                      onClick={() => setSourceKind(k)}
                      className={
                        'px-2 py-1 rounded text-xs ' +
                        (sourceKind === k
                          ? 'bg-blue-600 text-white'
                          : 'bg-zinc-100 dark:bg-zinc-800 text-zinc-700 dark:text-zinc-300')
                      }
                    >
                      {k}
                    </button>
                  ))}
                </div>
              </div>
              {sourceKind === 'link' ? (
                <div>
                  <label className="block text-xs text-zinc-500 mb-1">URL <span className="text-red-500">*</span></label>
                  <input
                    value={url}
                    onChange={(e) => setUrl(e.target.value)}
                    placeholder="https://…"
                    className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                  />
                </div>
              ) : (
                <div>
                  <label className="block text-xs text-zinc-500 mb-1">Path <span className="text-red-500">*</span></label>
                  <input
                    value={path}
                    onChange={(e) => setPath(e.target.value)}
                    placeholder="docs/spec.pdf"
                    className="w-full px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                  />
                </div>
              )}
            </>
          )}
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
            disabled={!canSubmit}
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
