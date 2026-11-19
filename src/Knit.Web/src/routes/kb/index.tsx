import { useCallback, useMemo } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { z } from 'zod'
import { ResizableSplit } from '../../components/ResizableSplit'
import { KbTree, type SelectedKb } from '../../components/KbTree'
import { KbDetails } from '../../components/KbDetails'
import { KbFolderCard } from '../../components/KbFolderCard'
import type { KbBucket } from '../../api/kb'

const KB_BUCKETS: readonly KbBucket[] = ['adr', 'doc', 'feature', 'log', 'idea', 'source']

const kbSearchSchema = z.object({
  bucket: z.enum(KB_BUCKETS as unknown as [KbBucket, ...KbBucket[]]).optional(),
  slug: z.string().optional(),
  folder: z.string().optional(),
})

export const Route = createFileRoute('/kb/')({
  validateSearch: kbSearchSchema,
  component: KbScreen,
})

function KbScreen() {
  const search = Route.useSearch()
  const navigate = useNavigate()

  const selected: SelectedKb = useMemo(
    () =>
      search.bucket && search.slug
        ? { kind: 'entry', bucket: search.bucket, slug: search.slug }
        : search.folder !== undefined
          ? { kind: 'folder', subpath: search.folder }
          : { kind: 'folder', subpath: '' },
    [search.bucket, search.slug, search.folder],
  )

  const setSelected = useCallback(
    (sel: SelectedKb) => {
      if (sel.kind === 'entry') {
        void navigate({ to: '.', search: { bucket: sel.bucket, slug: sel.slug } as never })
      } else {
        void navigate({ to: '.', search: { folder: sel.subpath } as never })
      }
    },
    [navigate],
  )

  return (
    <ResizableSplit
      storageKey="kb"
      left={
        <KbTree selected={selected} onSelect={setSelected} />
      }
      right={
        selected?.kind === 'entry' ? (
          <KbDetails bucket={selected.bucket} slug={selected.slug} />
        ) : (
          <KbFolderCard subpath={selected?.subpath ?? ''} />
        )
      }
    />
  )
}
