import { useCallback, useEffect } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { ResizableSplit } from '../../components/ResizableSplit'
import { DesignsTree, type DesignsTab } from '../../components/DesignsTree'
import { DesignDetails } from '../../components/DesignDetails'
import { designsApi, designsQk } from '../../api/designs'
import { slugify } from '../../lib/slug'
import { useToasts } from '../../stores/toasts'

const searchSchema = z.object({
  tab: z.enum(['active', 'completed']).optional(),
  slug: z.string().optional(),
})

export const Route = createFileRoute('/designs/')({
  validateSearch: searchSchema,
  component: DesignsScreen,
})

function DesignsScreen() {
  const search = Route.useSearch()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)

  const tab: DesignsTab = search.tab ?? 'active'
  const slug = search.slug ?? null

  // A link that names only the slug (a ref, a search hit, a new tab) can't
  // know the tab; the entry can. Same key as the detail pane — no extra fetch.
  const entry = useQuery({
    queryKey: designsQk.entry(slug ?? ''),
    queryFn: () => designsApi.get(slug!),
    enabled: !!slug && !search.tab,
  })
  const completedEntry = !search.tab && entry.data?.completed === true
  useEffect(() => {
    if (completedEntry) void navigate({ to: '.', search: { tab: 'completed', slug } as never, replace: true })
  }, [completedEntry, navigate, slug])

  const selectTab = useCallback(
    (t: DesignsTab) => void navigate({ to: '.', search: { tab: t } as never }),
    [navigate],
  )
  const selectDoc = useCallback(
    (s: string) => {
      void navigate({ to: '.', search: { tab, slug: s } as never })
    },
    [navigate, tab],
  )
  const onArchived = useCallback(
    () => void navigate({ to: '.', search: { tab } as never }),
    [navigate, tab],
  )

  const newDesign = useCallback(async () => {
    const title = window.prompt('New design title:')
    if (!title || !title.trim()) return
    try {
      const created = await designsApi.create({ slug: slugify(title.trim()), title: title.trim() })
      push('success', `Created ${created.slug}`)
      void qc.invalidateQueries({ queryKey: ['designs'] })
      void navigate({ to: '.', search: { tab: 'active', slug: created.slug } as never })
    } catch (err) {
      push('error', `Create failed: ${String(err)}`)
    }
  }, [navigate, qc, push])

  return (
    <ResizableSplit
      storageKey="designs"
      left={
        <DesignsTree
          tab={tab}
          selectedSlug={slug}
          onSelectTab={selectTab}
          onSelectDoc={selectDoc}
          onNewDesign={() => void newDesign()}
        />
      }
      right={
        slug ? (
          <DesignDetails slug={slug} onArchived={onArchived} />
        ) : (
          <div className="h-full flex items-center justify-center text-sm text-zinc-500">
            Select a design to view it.
          </div>
        )
      }
    />
  )
}
