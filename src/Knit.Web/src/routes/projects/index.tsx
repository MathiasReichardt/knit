import { useCallback } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { z } from 'zod'
import { ResizableSplit } from '../../components/ResizableSplit'
import { ProjectsTree, type MilestoneRef, type ProjectDocRef } from '../../components/ProjectsTree'
import { DetailsPane } from '../../components/DetailsPane'
import { MilestoneDetailPanel } from '../../components/MilestoneDetailPanel'
import { ProjectDocDetailPanel } from '../../components/ProjectDocDetailPanel'

// `id` selects an item (item details); `mp`/`ms` select a milestone sub-doc;
// `doc` (=`<project>/<path>`) selects a readme/roadmap/free-form doc. They're
// mutually exclusive — picking one clears the others.
const searchSchema = z.object({
  id: z.string().optional(),
  mp: z.string().optional(),
  ms: z.string().optional(),
  doc: z.string().optional(),
})

export const Route = createFileRoute('/projects/')({
  validateSearch: searchSchema,
  component: ProjectsScreen,
})

function parseDoc(doc: string | undefined): ProjectDocRef | null {
  if (!doc) return null
  const slash = doc.indexOf('/')
  if (slash <= 0 || slash === doc.length - 1) return null
  return { project: doc.slice(0, slash), path: doc.slice(slash + 1) }
}

function ProjectsScreen() {
  const search = Route.useSearch()
  const navigate = useNavigate()
  const selectedId = search.id ?? null
  const selectedMilestone: MilestoneRef | null =
    search.mp && search.ms ? { project: search.mp, slug: search.ms } : null
  const selectedDoc = parseDoc(search.doc)

  const selectItem = useCallback(
    (id: string) => {
      void navigate({ to: '.', search: { id }, replace: false })
    },
    [navigate],
  )

  const selectMilestone = useCallback(
    (m: MilestoneRef) => {
      void navigate({ to: '.', search: { mp: m.project, ms: m.slug }, replace: false })
    },
    [navigate],
  )

  const selectDoc = useCallback(
    (project: string, path: string) => {
      void navigate({ to: '.', search: { doc: `${project}/${path}` }, replace: false })
    },
    [navigate],
  )

  const clearSelection = useCallback(() => {
    void navigate({ to: '.', search: {}, replace: false })
  }, [navigate])

  return (
    <ResizableSplit
      storageKey="projects"
      left={
        <ProjectsTree
          selectedId={selectedId}
          selectedMilestone={selectedMilestone}
          selectedDoc={selectedDoc}
          onSelectItem={selectItem}
          onSelectMilestone={selectMilestone}
          onSelectDoc={selectDoc}
        />
      }
      right={
        selectedDoc ? (
          <ProjectDocDetailPanel
            project={selectedDoc.project}
            path={selectedDoc.path}
            onSelectDoc={selectDoc}
            onCleared={clearSelection}
          />
        ) : selectedMilestone ? (
          <MilestoneDetailPanel
            project={selectedMilestone.project}
            slug={selectedMilestone.slug}
            onSelectItem={selectItem}
          />
        ) : (
          <DetailsPane itemId={selectedId} onSelectItem={selectItem} />
        )
      }
    />
  )
}
