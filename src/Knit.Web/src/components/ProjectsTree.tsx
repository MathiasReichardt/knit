import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import { dropOnlyWhereHovered } from '../lib/dndCollision'
import { DragGhost } from './DragGhost'
import {
  ArrowDown,
  ArrowRight,
  ArrowUp,
  ChevronDown,
  ChevronRight,
  ChevronsDownUp,
  ChevronsUpDown,
  FilePlus,
  FileText,
  Flag,
  Folder,
  FolderKanban,
  FolderPlus,
  GripVertical,
  House,
  ListTree,
  Map as MapIcon,
  Plus,
  Trash2,
} from 'lucide-react'
import { api, qk, type ItemQuery } from '../api/client'
import { projectsApi, projectsQk, type Milestone, type ProjectFolder } from '../api/projects'
import { ProjectDeleteDialog } from './ProjectDeleteDialog'
import { ItemDeleteDialog } from './ItemDeleteDialog'
import { CopySlugIconButton } from './CopyButton'
import { slugify } from '../lib/slug'
import { useDialogs } from '../stores/dialogs'
import { useNavigator } from '../stores/navigator'
import { useToasts } from '../stores/toasts'
import {
  HEALTH_COLOR,
  HEALTH_ICON,
  HEALTH_LABEL,
  STATE_COLOR,
  STATE_ICON,
  TYPE_COLOR,
  TYPE_ICON,
} from '../lib/glyphs'
import {
  buildMilestoneMap,
  collectIds,
  externalWorkRoots,
  splitProjectChildren,
} from '../lib/projectTree'
import { formatShortDate } from '../lib/shortDate'
import { RowLink } from './RowLink'
import {
  buildDocTree,
  docDropTarget,
  folderContainerOf,
  type DocTreeNode,
} from '../lib/projectDocTree'
import { reorderMilestones } from '../lib/milestoneOrder'
import { useTreeOpen } from '../stores/treeOpen'
import type { ItemHealth, TreeNode } from '../api/types'

export interface MilestoneRef {
  project: string
  slug: string
}

export interface ProjectDocRef {
  project: string
  // Project-folder-relative path (readme.md, roadmap.md, notes.md, sub/readme.md).
  path: string
}

interface Props {
  selectedId: string | null
  selectedMilestone: MilestoneRef | null
  selectedDoc: ProjectDocRef | null
  onSelectItem: (id: string) => void
  onSelectMilestone: (m: MilestoneRef) => void
  onSelectDoc: (project: string, path: string) => void
}

const PROJECTS_QUERY: ItemQuery = { excludeState: ['dismissed'] }

// The Projects (PM) nav: the same items live here as in the dev tree, but
// rooted at project nodes and split into Documents / Todos / Work items. It
// always shows todos (unlike the dev view's lens default) and never applies
// the audience lenses. Work-items composition follows lib/projectTree.
export function ProjectsTree({ selectedId, selectedMilestone, selectedDoc, onSelectItem, onSelectMilestone, onSelectDoc }: Props) {
  const openNewItem = useDialogs((s) => s.openNewItem)
  const [removeTarget, setRemoveTarget] = useState<{ slug: string; title: string } | null>(null)
  // Expanded nodes, persisted per repo. One namespace covers both
  // levels of this tree: project sections (keyed by project item id, or
  // `folder:<slug>` for an orphan doc folder) and the work-item / todo nodes
  // inside them (keyed by item id). A project item is only ever a section and
  // never an `ItemNode`, so the two key spaces can't collide.
  const { ready, expanded, toggle, replace, expand, prune } = useTreeOpen('projects')
  const foldersQ = useQuery({ queryKey: projectsQk.list, queryFn: projectsApi.list })
  const treeQ = useQuery({
    queryKey: qk.treeFiltered(PROJECTS_QUERY),
    queryFn: () => api.tree(PROJECTS_QUERY),
  })

  // Milestone backlinks power both the in-place badge and the cross-project
  // work-items roots — one detail fetch per milestone, cached + SSE-refreshed.
  const folders = foldersQ.data ?? []
  const pairs = folders.flatMap((f) => f.milestones.map((m) => ({ project: f.slug, slug: m.slug })))
  const details = useQueries({
    queries: pairs.map((p) => ({
      queryKey: projectsQk.milestone(p.project, p.slug),
      queryFn: () => projectsApi.milestone(p.project, p.slug),
      staleTime: 30_000,
    })),
    combine: (results) => results.flatMap((r) => (r.data ? [r.data] : [])),
  })
  const { itemMilestone, linkedByProject } = buildMilestoneMap(details)

  // Prune stale keys. This tree's query excludes dismissed items, so the node
  // set is not the whole repo — but a dismissed item is never *rendered* here,
  // so its expand state is dead for this tree either way; un-dismissing brings
  // it back collapsed like anything new. Folder and doc keys come from the
  // project list, which is unfiltered.
  const aliveKeys = useMemo(() => {
    if (!treeQ.data || !foldersQ.data) return null
    const alive = new Set<string>(collectIds(treeQ.data))
    for (const f of foldersQ.data) {
      alive.add(`folder:${f.slug}`)
      // Only folder rows are expandable, and they are synthesized from the
      // file paths — walk the built tree rather than `files`, or every doc
      // folder would look deleted.
      const walk = (nodes: DocTreeNode[]) => {
        for (const n of nodes) {
          alive.add(docKey(f.slug, n.path))
          walk(n.children)
        }
      }
      walk(buildDocTree(f.files))
    }
    return alive
  }, [treeQ.data, foldersQ.data])
  useEffect(() => {
    if (!ready || !aliveKeys) return
    prune(aliveKeys)
  }, [ready, aliveKeys, prune])

  if (foldersQ.isLoading || treeQ.isLoading || !ready) {
    return <div className="p-4 text-sm text-zinc-500">Loading…</div>
  }

  const fullTree = treeQ.data ?? []
  const projectNodes = fullTree.filter((n) => n.item.type === 'project')
  const folderBySlug = new Map(folders.map((f) => [f.slug, f]))
  const referenced = new Set(
    projectNodes.map((n) => n.item.project).filter((s): s is string => !!s),
  )
  const orphanFolders = folders.filter((f) => !referenced.has(f.slug))
  const allKeys = [
    ...projectNodes.map((p) => p.item.id),
    ...orphanFolders.map((f) => `folder:${f.slug}`),
  ]

  if (projectNodes.length === 0 && orphanFolders.length === 0) {
    return (
      <div className="p-6 text-sm text-zinc-500 space-y-3">
        <p>No projects yet.</p>
        <button
          type="button"
          onClick={() => openNewItem({ type: 'project', allowedTypes: ['project'] })}
          className="inline-flex items-center gap-1.5 rounded border border-zinc-300 dark:border-zinc-700 px-2 py-1 text-xs hover:bg-zinc-100 dark:hover:bg-zinc-900"
        >
          <Plus className="h-3.5 w-3.5" aria-hidden />
          New project
        </button>
        <p className="text-xs text-zinc-400">
          The doc folder (roadmap, milestones) is scaffolded with{' '}
          <code className="font-mono">knit project new</code>.
        </p>
      </div>
    )
  }

  return (
    <div className="flex-1 min-h-0 overflow-auto py-1">
      <div className="flex items-center justify-between px-2 py-1.5">
        <span className="text-[10px] font-semibold uppercase tracking-wide text-zinc-400">
          Projects
        </span>
        <span className="flex items-center gap-0.5">
          <button
            type="button"
            onClick={() => expand(allKeys)}
            title="Expand all projects"
            aria-label="Expand all projects"
            className="shrink-0 p-1 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
          >
            <ChevronsUpDown className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={() => replace([])}
            title="Collapse all projects"
            aria-label="Collapse all projects"
            className="shrink-0 p-1 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
          >
            <ChevronsDownUp className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={() => openNewItem({ type: 'project', allowedTypes: ['project'] })}
            title="New project"
            aria-label="New project"
            className="shrink-0 p-1 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
          >
            <Plus className="h-3.5 w-3.5" />
          </button>
        </span>
      </div>
      {projectNodes.map((p) => {
        const folder = p.item.project ? folderBySlug.get(p.item.project) : undefined
        const { todos, work } = splitProjectChildren(p)
        const linked = folder ? linkedByProject.get(folder.slug) ?? new Set<string>() : new Set<string>()
        const extRoots = externalWorkRoots(fullTree, collectIds([p]), linked)
        return (
          <ProjectSection
            key={p.item.id}
            project={p}
            folder={folder}
            todos={todos}
            workRoots={[...work, ...extRoots]}
            itemMilestone={itemMilestone}
            selectedId={selectedId}
            selectedMilestone={selectedMilestone}
            selectedDoc={selectedDoc}
            collapsed={!expanded.has(p.item.id)}
            onToggleCollapsed={() => toggle(p.item.id)}
            expanded={expanded}
            onToggleNode={toggle}
            onSelectItem={onSelectItem}
            onSelectMilestone={onSelectMilestone}
            onSelectDoc={onSelectDoc}
            onRemove={setRemoveTarget}
          />
        )
      })}
      {orphanFolders.map((f) => (
        <ProjectSection
          key={`folder:${f.slug}`}
          folder={f}
          docOnlyTitle={f.slug}
          todos={[]}
          workRoots={[]}
          itemMilestone={itemMilestone}
          selectedId={selectedId}
          selectedMilestone={selectedMilestone}
          selectedDoc={selectedDoc}
          collapsed={!expanded.has(`folder:${f.slug}`)}
          onToggleCollapsed={() => toggle(`folder:${f.slug}`)}
          expanded={expanded}
          onToggleNode={toggle}
          onSelectItem={onSelectItem}
          onSelectMilestone={onSelectMilestone}
          onSelectDoc={onSelectDoc}
          onRemove={setRemoveTarget}
        />
      ))}
      {removeTarget && (
        <ProjectDeleteDialog
          slug={removeTarget.slug}
          title={removeTarget.title}
          onClose={() => setRemoveTarget(null)}
          onDeleted={() => setRemoveTarget(null)}
        />
      )}
    </div>
  )
}

interface SectionProps {
  project?: TreeNode
  folder?: ProjectFolder
  docOnlyTitle?: string
  todos: TreeNode[]
  workRoots: TreeNode[]
  itemMilestone: Map<string, string>
  selectedId: string | null
  selectedMilestone: MilestoneRef | null
  selectedDoc: ProjectDocRef | null
  collapsed: boolean
  onToggleCollapsed: () => void
  // Expand state of the item nodes inside this section, from the same store.
  expanded: ReadonlySet<string>
  onToggleNode: (id: string) => void
  onSelectItem: (id: string) => void
  onSelectMilestone: (m: MilestoneRef) => void
  onSelectDoc: (project: string, path: string) => void
  onRemove: (target: { slug: string; title: string }) => void
}

function ProjectSection({
  project,
  folder,
  docOnlyTitle,
  todos,
  workRoots,
  itemMilestone,
  selectedId,
  selectedMilestone,
  selectedDoc,
  collapsed,
  onToggleCollapsed,
  expanded,
  onToggleNode,
  onSelectItem,
  onSelectMilestone,
  onSelectDoc,
  onRemove,
}: SectionProps) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)
  const setUnder = useNavigator((s) => s.setUnder)
  const openNewItem = useDialogs((s) => s.openNewItem)
  const openNewMilestone = useDialogs((s) => s.openNewMilestone)
  const openNewProjectDoc = useDialogs((s) => s.openNewProjectDoc)
  const title = project?.item.title ?? docOnlyTitle ?? folder?.slug ?? '(project)'
  const health = project?.item.health ?? null
  const projectSlug = folder?.slug ?? project?.item.project ?? ''

  // Scaffold the missing doc folder for a project item that has none (the
  // reverse orphan of an orphan folder) — co-creates via the atomic path so the
  // item↔folder link lands too. prompt-before-create: the button is the prompt.
  const scaffoldFolder = useMutation({
    mutationFn: () =>
      projectsApi.createProject({
        slug: project!.item.project || slugify(project!.item.title),
        title: project!.item.title,
        linkItemId: project!.item.id,
      }),
    onSuccess: (r) => {
      push('success', `Scaffolded doc folder for ${r.slug}`)
      void qc.invalidateQueries({ queryKey: ['projects'] })
    },
    onError: (e) => push('error', `Scaffold failed: ${String(e)}`),
  })

  // Scaffold roadmap.md standalone (never overwrites the readme). Only offered
  // when the folder has no roadmap yet.
  const addRoadmap = useMutation({
    mutationFn: () => projectsApi.scaffoldRoadmap(projectSlug),
    onSuccess: () => {
      push('success', `Added roadmap to ${projectSlug}`)
      void qc.invalidateQueries({ queryKey: ['projects'] })
      onSelectDoc(projectSlug, 'roadmap.md')
    },
    onError: (e) => push('error', `Add roadmap failed: ${String(e)}`),
  })

  const viewOnTree = () => {
    if (!project) return
    setUnder(project.item.id)
    void navigate({ to: '/items', search: { id: project.item.id } as never })
  }

  const docActive = (path: string) =>
    selectedDoc?.project === projectSlug && selectedDoc?.path === path

  const addTodo = () =>
    project &&
    openNewItem({
      type: 'todo',
      parentId: project.item.id,
      allowedTypes: ['todo'],
      stayOnCreate: true,
    })

  return (
    <div className="mb-2">
      <div className="group flex items-center gap-2 px-2 py-1.5 sticky top-0 bg-white dark:bg-zinc-950 z-10">
        <button
          type="button"
          onClick={onToggleCollapsed}
          aria-label={collapsed ? 'Expand project' : 'Collapse project'}
          title={collapsed ? 'Expand project' : 'Collapse project'}
          className="shrink-0 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200"
        >
          {collapsed ? <ChevronRight className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
        </button>
        <FolderKanban className="h-4 w-4 text-cyan-600 dark:text-cyan-400 shrink-0" aria-hidden />
        <RowLink
          to="/projects"
          search={{ id: project?.item.id }}
          onSelect={() => project && onSelectItem(project.item.id)}
          disabled={!project}
          className="min-w-0 flex-1 text-left font-medium text-sm truncate"
        >
          {title}
        </RowLink>
        {health && <HealthDot health={health} />}
        {projectSlug && (
          <span className="shrink-0 opacity-0 group-hover:opacity-100 focus-within:opacity-100 transition-opacity">
            <CopySlugIconButton value={projectSlug} title={`Copy project slug: ${projectSlug}`} />
          </span>
        )}
        {project && (
          <button
            type="button"
            onClick={viewOnTree}
            title="View on the items tree (scoped to this project)"
            aria-label="View on the items tree"
            className="shrink-0 p-1 rounded text-zinc-400 opacity-0 group-hover:opacity-100 focus:opacity-100 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
          >
            <ListTree className="h-3.5 w-3.5" />
          </button>
        )}
        {project && !folder && (
          <button
            type="button"
            disabled={scaffoldFolder.isPending}
            onClick={() => scaffoldFolder.mutate()}
            title="Scaffold the doc folder (readme) and link it to this project item"
            aria-label="Scaffold doc folder"
            className="shrink-0 p-1 rounded text-zinc-400 opacity-0 group-hover:opacity-100 focus:opacity-100 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800 disabled:opacity-50"
          >
            <FolderPlus className="h-3.5 w-3.5" />
          </button>
        )}
        {projectSlug && (
          <button
            type="button"
            onClick={() => onRemove({ slug: projectSlug, title })}
            title="Remove project (item + doc folder; children re-rooted)"
            aria-label="Remove project"
            className="shrink-0 p-1 rounded text-zinc-400 opacity-0 group-hover:opacity-100 focus:opacity-100 hover:text-red-600 dark:hover:text-red-400 hover:bg-zinc-200 dark:hover:bg-zinc-800"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {!collapsed && folder && (
        <Section
          label="Documents"
          action={
            projectSlug && (
              <span className="flex items-center gap-0.5">
                <button
                  type="button"
                  onClick={() => openNewProjectDoc({ project: projectSlug })}
                  title="New document"
                  aria-label="New document"
                  className="p-0.5 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
                >
                  <FilePlus className="h-3.5 w-3.5" />
                </button>
                {!folder.hasRoadmap && (
                  <button
                    type="button"
                    disabled={addRoadmap.isPending}
                    onClick={() => addRoadmap.mutate()}
                    title="Add roadmap (required before adding milestones)"
                    aria-label="Add roadmap"
                    className="p-0.5 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800 disabled:opacity-50"
                  >
                    <MapIcon className="h-3.5 w-3.5" />
                  </button>
                )}
              </span>
            )
          }
        >
          {folder.hasReadme && (
            <DocRow
              active={docActive('readme.md')}
              icon={<House className="h-3.5 w-3.5 text-zinc-500" aria-hidden />}
              label="Overview"
              doc={`${projectSlug}/readme.md`}
              onClick={() => onSelectDoc(projectSlug, 'readme.md')}
            />
          )}
          {folder.hasRoadmap ? (
            <RoadmapSpine
              project={projectSlug}
              roadmapActive={docActive('roadmap.md')}
              milestones={folder.milestones}
              selectedMilestone={selectedMilestone}
              onSelectRoadmap={() => onSelectDoc(projectSlug, 'roadmap.md')}
              onSelectMilestone={onSelectMilestone}
              onNewMilestone={() => openNewMilestone({ project: projectSlug })}
            />
          ) : (
            folder.milestones.length > 0 && (
              <MilestoneDndList
                project={projectSlug}
                milestones={folder.milestones}
                indent={24}
                selectedMilestone={selectedMilestone}
                onSelectMilestone={onSelectMilestone}
              />
            )
          )}
          {folder.files.length > 0 && (
            <DocDndTree
              project={projectSlug}
              files={folder.files}
              docActive={docActive}
              onSelectDoc={onSelectDoc}
              expanded={expanded}
              onToggle={onToggleNode}
            />
          )}
          {!folder.hasReadme && !folder.hasRoadmap && folder.milestones.length === 0 && folder.files.length === 0 && (
            <EmptyHint>No documents.</EmptyHint>
          )}
        </Section>
      )}

      {!collapsed && project && (
        <>
          <Section
            label="Todos"
            action={
              <button
                type="button"
                onClick={addTodo}
                title="Add todo to this project"
                aria-label="Add todo to this project"
                className="p-0.5 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
              >
                <Plus className="h-3.5 w-3.5" />
              </button>
            }
          >
            {todos.length === 0 ? (
              <button
                type="button"
                onClick={addTodo}
                className="flex items-center gap-1.5 px-4 py-1 text-xs italic text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-300"
              >
                <Plus className="h-3.5 w-3.5" aria-hidden />
                No todos — add one
              </button>
            ) : (
              todos.map((n) => (
                <ItemNode
                  key={n.item.id}
                  node={n}
                  depth={0}
                  itemMilestone={itemMilestone}
                  selectedId={selectedId}
                  onSelectItem={onSelectItem}
                  expanded={expanded}
                  onToggle={onToggleNode}
                />
              ))
            )}
          </Section>
          <Section label="Work items">
            {workRoots.length === 0 ? (
              <EmptyHint>No work items.</EmptyHint>
            ) : (
              workRoots.map((n) => (
                <ItemNode
                  key={n.item.id}
                  node={n}
                  depth={0}
                  itemMilestone={itemMilestone}
                  selectedId={selectedId}
                  onSelectItem={onSelectItem}
                  expanded={expanded}
                  onToggle={onToggleNode}
                />
              ))
            )}
          </Section>
        </>
      )}
    </div>
  )
}

function Section({
  label,
  action,
  children,
}: {
  label: string
  action?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <div className="group mb-1">
      <div className="px-2 pt-1 pb-0.5 flex items-center">
        <span className="text-[10px] font-semibold uppercase tracking-wide text-zinc-400">{label}</span>
        {action && (
          <span className="ml-auto opacity-0 group-hover:opacity-100 focus-within:opacity-100 transition-opacity">
            {action}
          </span>
        )}
      </div>
      {children}
    </div>
  )
}

function EmptyHint({ children }: { children: React.ReactNode }) {
  return <div className="px-4 py-1 text-xs text-zinc-400 italic">{children}</div>
}

function DocRow({
  icon,
  label,
  meta,
  doc,
  active,
  onClick,
}: {
  icon: React.ReactNode
  label: string
  meta?: React.ReactNode
  doc: string
  active?: boolean
  onClick: () => void
}) {
  return (
    <RowLink
      to="/projects"
      search={{ doc }}
      onSelect={onClick}
      className={
        'flex w-full items-center gap-2 pl-6 pr-2 py-1 text-left text-sm ' +
        (active
          ? 'bg-blue-100 dark:bg-blue-900/40'
          : 'hover:bg-zinc-100 dark:hover:bg-zinc-900')
      }
    >
      {icon}
      <span className="truncate flex-1">{label}</span>
      {meta}
    </RowLink>
  )
}

// The free-form docs of a project, grouped into a folder tree with the same
// drag-and-drop feel as the items tree (dnd-kit + before/inside/after drop
// bands). Docs are name-sorted, so before/after only pick the destination
// *container* (root vs a folder); inside a folder node moves the doc into it.
// Every move reduces to the doc rename/move endpoint. One DndContext per
// project scopes drags to that project's folder.
// Doc-folder rows share the projects namespace but live in a path space, not
// an id space, so they carry a prefix: `doc:<project>/<path>`.
const docKey = (project: string, path: string) => `doc:${project}/${path}`

function DocDndTree({
  project,
  files,
  docActive,
  onSelectDoc,
  expanded,
  onToggle,
}: {
  project: string
  files: string[]
  docActive: (path: string) => boolean
  onSelectDoc: (project: string, path: string) => void
  expanded: ReadonlySet<string>
  onToggle: (key: string) => void
}) {
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)
  const [dragPath, setDragPath] = useState<string | null>(null)
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor),
  )
  const nodes = buildDocTree(files)

  const onDragEnd = async (e: DragEndEvent) => {
    const src = dragPath
    setDragPath(null)
    if (!src || !e.over) return
    const container = (e.over.data.current as { container?: string } | undefined)?.container
    if (container === undefined) return
    const newPath = docDropTarget(src, container)
    if (!newPath) return
    try {
      const r = await projectsApi.renameDoc(project, src, newPath)
      push('success', `Moved → ${r.newPath}`)
      void qc.invalidateQueries({ queryKey: ['projects'] })
    } catch (err) {
      push('error', `Move failed: ${String(err)}`)
    }
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={dropOnlyWhereHovered}
      onDragStart={(e) => setDragPath(String(e.active.id))}
      onDragEnd={onDragEnd}
      onDragCancel={() => setDragPath(null)}
    >
      {nodes.map((n) => (
        <DocNode
          key={n.path}
          node={n}
          depth={0}
          project={project}
          dragging={dragPath !== null}
          draggedPath={dragPath}
          docActive={docActive}
          onSelectDoc={onSelectDoc}
          expanded={expanded}
          onToggle={onToggle}
        />
      ))}
      <DocRootDrop active={dragPath !== null} />
      <DragOverlay>
        {dragPath ? <DocDragGhost path={dragPath} isFolder={!files.includes(dragPath)} /> : null}
      </DragOverlay>
    </DndContext>
  )
}

function DocNode({
  node,
  depth,
  project,
  dragging,
  draggedPath,
  docActive,
  onSelectDoc,
  expanded,
  onToggle,
}: {
  node: DocTreeNode
  depth: number
  project: string
  dragging: boolean
  draggedPath: string | null
  docActive: (path: string) => boolean
  onSelectDoc: (project: string, path: string) => void
  expanded: ReadonlySet<string>
  onToggle: (key: string) => void
}) {
  const open = expanded.has(docKey(project, node.path))
  const { attributes, listeners, setNodeRef: setDragRef, isDragging } = useDraggable({ id: node.path })
  const beingDragged = draggedPath === node.path
  const showDrops = dragging && !beingDragged
  const indent = 24 + depth * 14
  const active = docActive(node.path)

  const Icon = node.isFolder ? Folder : FileText

  return (
    <div>
      <div
        className={
          'group relative flex items-center gap-1.5 pr-2 py-1 text-sm ' +
          (active ? 'bg-blue-100 dark:bg-blue-900/40' : 'hover:bg-zinc-100 dark:hover:bg-zinc-900') +
          (beingDragged || isDragging ? ' opacity-40' : '')
        }
        style={{ paddingLeft: indent }}
      >
        <DocDropBand pos="before" id={`docbefore:${node.path}`} container={node.container} active={showDrops} indent={indent} />
        {node.isFolder && (
          <DocDropBand pos="inside" id={`docinside:${node.path}`} container={folderContainerOf(node)} active={showDrops} indent={indent} />
        )}
        <DocDropBand pos="after" id={`docafter:${node.path}`} container={node.container} active={showDrops} indent={indent} />
        <button
          ref={setDragRef}
          {...attributes}
          {...listeners}
          type="button"
          title="Drag to move into a folder or back to root"
          aria-label="Drag handle"
          className="opacity-0 group-hover:opacity-100 focus:opacity-100 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 cursor-grab active:cursor-grabbing shrink-0"
        >
          <GripVertical className="h-3.5 w-3.5" />
        </button>
        {node.isFolder ? (
          <button
            type="button"
            onClick={() => onToggle(docKey(project, node.path))}
            aria-label={open ? 'Collapse' : 'Expand'}
            className="shrink-0 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200"
          >
            {open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
          </button>
        ) : (
          <span aria-hidden className="w-3.5 shrink-0" />
        )}
        <Icon className={`h-3.5 w-3.5 shrink-0 ${node.isFolder ? 'text-amber-500' : 'text-zinc-500'}`} aria-hidden />
        <RowLink
          to="/projects"
          search={{ doc: `${project}/${node.path}` }}
          onSelect={() => onSelectDoc(project, node.path)}
          className="min-w-0 flex-1 text-left truncate"
        >
          {node.name}
        </RowLink>
      </div>
      {node.isFolder &&
        open &&
        node.children.map((c) => (
          <DocNode
            key={c.path}
            node={c}
            depth={depth + 1}
            project={project}
            dragging={dragging}
            draggedPath={draggedPath}
            docActive={docActive}
            onSelectDoc={onSelectDoc}
            expanded={expanded}
            onToggle={onToggle}
          />
        ))}
    </div>
  )
}

// One of a doc row's stacked drop zones. before/after target the row's own
// container; inside (folder rows only) targets the folder. Bands are
// pointer-events-none so normal clicks pass through — dnd-kit resolves the
// target from droppable rects (nearest center → equal thirds).
function DocDropBand({
  pos,
  id,
  container,
  active,
  indent,
}: {
  pos: 'before' | 'inside' | 'after'
  id: string
  container: string
  active: boolean
  indent: number
}) {
  const { isOver, setNodeRef } = useDroppable({ id, data: { container } })
  const band = pos === 'before' ? 'top-0 h-1/3' : pos === 'after' ? 'bottom-0 h-1/3' : 'top-1/3 h-1/3'
  const Icon = pos === 'before' ? ArrowUp : pos === 'after' ? ArrowDown : ArrowRight
  const show = active && isOver
  return (
    <div ref={setNodeRef} aria-hidden className={`pointer-events-none absolute inset-x-0 ${band}`}>
      {show && (
        <>
          {pos === 'inside' ? (
            <span className="absolute -inset-y-full inset-x-0 rounded-sm ring-1 ring-inset ring-blue-500 bg-blue-500/10" />
          ) : (
            <span className={`absolute inset-x-0 ${pos === 'before' ? 'top-0' : 'bottom-0'} h-0.5 bg-blue-500`} />
          )}
          <span
            style={{ left: indent }}
            className={
              'absolute inline-flex h-4 w-4 items-center justify-center rounded-full bg-blue-500 text-white shadow ' +
              (pos === 'before'
                ? 'top-0 -translate-y-1/2'
                : pos === 'after'
                  ? 'bottom-0 translate-y-1/2'
                  : 'top-1/2 -translate-y-1/2')
            }
          >
            <Icon className="h-3 w-3" />
          </span>
        </>
      )}
    </div>
  )
}

// A dashed strip shown only while dragging, so a doc buried in a folder can be
// pulled back to the project root even when no root-level doc row exists.
function DocRootDrop({ active }: { active: boolean }) {
  const { isOver, setNodeRef } = useDroppable({ id: 'docroot', data: { container: '' } })
  if (!active) return null
  return (
    <div
      ref={setNodeRef}
      className={
        'mx-2 my-1 rounded border border-dashed px-3 py-1 text-[11px] ' +
        (isOver
          ? 'border-blue-500 bg-blue-500/10 text-blue-600 dark:text-blue-300'
          : 'border-zinc-300 dark:border-zinc-700 text-zinc-400')
      }
    >
      Move to project root
    </div>
  )
}

// The roadmap as the project's navigational spine: the roadmap doc as
// a first-class node with the project's milestones nested beneath it. The
// nesting is derived from folder layout (roadmap.md + milestones/ are siblings
// on disk) and is display-only. The chevron collapses the milestone list; the
// label opens the roadmap body.
function RoadmapSpine({
  project,
  roadmapActive,
  milestones,
  selectedMilestone,
  onSelectRoadmap,
  onSelectMilestone,
  onNewMilestone,
}: {
  project: string
  roadmapActive: boolean
  milestones: Milestone[]
  selectedMilestone: MilestoneRef | null
  onSelectRoadmap: () => void
  onSelectMilestone: (m: MilestoneRef) => void
  onNewMilestone: () => void
}) {
  const [open, setOpen] = useState(true)
  return (
    <div>
      <div
        className={
          'group flex items-center gap-1.5 pr-2 py-1 text-sm ' +
          (roadmapActive ? 'bg-blue-100 dark:bg-blue-900/40' : 'hover:bg-zinc-100 dark:hover:bg-zinc-900')
        }
        style={{ paddingLeft: 24 }}
      >
        {milestones.length > 0 ? (
          <button
            type="button"
            onClick={() => setOpen((v) => !v)}
            aria-label={open ? 'Collapse milestones' : 'Expand milestones'}
            className="shrink-0 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200"
          >
            {open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
          </button>
        ) : (
          <span aria-hidden className="w-3.5 shrink-0" />
        )}
        <MapIcon className="h-3.5 w-3.5 text-zinc-500 shrink-0" aria-hidden />
        <RowLink
          to="/projects"
          search={{ doc: `${project}/roadmap.md` }}
          onSelect={onSelectRoadmap}
          className="min-w-0 flex-1 text-left truncate"
        >
          roadmap
        </RowLink>
        <button
          type="button"
          onClick={onNewMilestone}
          title="New milestone"
          aria-label="New milestone"
          className="shrink-0 p-0.5 rounded text-zinc-400 opacity-0 group-hover:opacity-100 focus:opacity-100 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
        >
          <Plus className="h-3.5 w-3.5" />
        </button>
      </div>
      {open && milestones.length > 0 && (
        <MilestoneDndList
          project={project}
          milestones={milestones}
          indent={38}
          selectedMilestone={selectedMilestone}
          onSelectMilestone={onSelectMilestone}
        />
      )}
    </div>
  )
}

// Drag-and-drop reordering for a project's milestones, sharing the
// items/doc-tree dnd-kit mechanism (before/after drop bands + grip handle).
// Milestones carry a manual rank, so a drop genuinely reorders; the server owns
// the rank strings (projectsApi.reorderMilestone sends the full slug order).
// One DndContext per project scopes the drag.
function MilestoneDndList({
  project,
  milestones,
  indent,
  selectedMilestone,
  onSelectMilestone,
}: {
  project: string
  milestones: Milestone[]
  indent: number
  selectedMilestone: MilestoneRef | null
  onSelectMilestone: (m: MilestoneRef) => void
}) {
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)
  const [dragSlug, setDragSlug] = useState<string | null>(null)
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor),
  )
  const order = milestones.map((m) => m.slug)

  const onDragEnd = async (e: DragEndEvent) => {
    const src = dragSlug
    setDragSlug(null)
    if (!src || !e.over) return
    const data = e.over.data.current as { slug?: string; pos?: 'before' | 'after' } | undefined
    if (!data?.slug || !data.pos) return
    const next = reorderMilestones(order, src, data.slug, data.pos)
    if (!next) return
    try {
      await projectsApi.reorderMilestone(project, next)
      void qc.invalidateQueries({ queryKey: ['projects'] })
    } catch (err) {
      push('error', `Reorder failed: ${String(err)}`)
    }
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={dropOnlyWhereHovered}
      onDragStart={(e) => setDragSlug(String(e.active.id))}
      onDragEnd={onDragEnd}
      onDragCancel={() => setDragSlug(null)}
    >
      {milestones.map((m) => (
        <MilestoneRow
          key={m.slug}
          milestone={m}
          indent={indent}
          dragging={dragSlug !== null}
          beingDragged={dragSlug === m.slug}
          active={selectedMilestone?.project === project && selectedMilestone?.slug === m.slug}
          project={project}
          onSelect={() => onSelectMilestone({ project, slug: m.slug })}
        />
      ))}
      <DragOverlay>
        {dragSlug ? (
          <MilestoneDragGhost title={milestones.find((m) => m.slug === dragSlug)?.title ?? dragSlug} />
        ) : null}
      </DragOverlay>
    </DndContext>
  )
}

function MilestoneRow({
  milestone,
  project,
  indent,
  dragging,
  beingDragged,
  active,
  onSelect,
}: {
  milestone: Milestone
  project: string
  indent: number
  dragging: boolean
  beingDragged: boolean
  active: boolean
  onSelect: () => void
}) {
  const { attributes, listeners, setNodeRef: setDragRef, isDragging } = useDraggable({ id: milestone.slug })
  const showDrops = dragging && !beingDragged
  const target = formatShortDate(milestone.target)
  return (
    <div
      className={
        'group relative flex items-center gap-1.5 pr-2 py-1 text-sm ' +
        (active ? 'bg-blue-100 dark:bg-blue-900/40' : 'hover:bg-zinc-100 dark:hover:bg-zinc-900') +
        (beingDragged || isDragging ? ' opacity-40' : '')
      }
      style={{ paddingLeft: indent }}
    >
      <MilestoneDropBand pos="before" id={`msbefore:${milestone.slug}`} slug={milestone.slug} active={showDrops} indent={indent} />
      <MilestoneDropBand pos="after" id={`msafter:${milestone.slug}`} slug={milestone.slug} active={showDrops} indent={indent} />
      <button
        ref={setDragRef}
        {...attributes}
        {...listeners}
        type="button"
        title="Drag to reorder"
        aria-label="Drag handle"
        className="opacity-0 group-hover:opacity-100 focus:opacity-100 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 cursor-grab active:cursor-grabbing shrink-0"
      >
        <GripVertical className="h-3.5 w-3.5" />
      </button>
      <Flag className="h-3.5 w-3.5 text-cyan-600 dark:text-cyan-400 shrink-0" aria-hidden />
      <RowLink
        to="/projects"
        search={{ mp: project, ms: milestone.slug }}
        onSelect={onSelect}
        className="min-w-0 flex-1 text-left truncate"
      >
        {milestone.title}
      </RowLink>
      <span className="flex items-center gap-1.5 text-[11px] text-zinc-400 shrink-0">
        {milestone.state !== 'planned' && <span>{milestone.state}</span>}
        {target && <span title={target.full}>{target.short}</span>}
        {milestone.linkedCount > 0 && <span>· {milestone.linkedCount}</span>}
      </span>
    </div>
  )
}

// A milestone row's stacked before/after drop zones — the reorder counterpart
// of DocDropBand. Each targets a slug + position; pointer-events-none so a
// normal click still selects the milestone.
function MilestoneDropBand({
  pos,
  id,
  slug,
  active,
  indent,
}: {
  pos: 'before' | 'after'
  id: string
  slug: string
  active: boolean
  indent: number
}) {
  const { isOver, setNodeRef } = useDroppable({ id, data: { slug, pos } })
  const band = pos === 'before' ? 'top-0 h-1/2' : 'bottom-0 h-1/2'
  const Icon = pos === 'before' ? ArrowUp : ArrowDown
  const show = active && isOver
  return (
    <div ref={setNodeRef} aria-hidden className={`pointer-events-none absolute inset-x-0 ${band}`}>
      {show && (
        <>
          <span className={`absolute inset-x-0 ${pos === 'before' ? 'top-0' : 'bottom-0'} h-0.5 bg-blue-500`} />
          <span
            style={{ left: indent }}
            className={
              'absolute inline-flex h-4 w-4 items-center justify-center rounded-full bg-blue-500 text-white shadow ' +
              (pos === 'before' ? 'top-0 -translate-y-1/2' : 'bottom-0 translate-y-1/2')
            }
          >
            <Icon className="h-3 w-3" />
          </span>
        </>
      )}
    </div>
  )
}

function HealthDot({ health }: { health: ItemHealth }) {
  const Icon = HEALTH_ICON[health]
  return (
    <span title={HEALTH_LABEL[health]} aria-label={HEALTH_LABEL[health]} className="shrink-0 inline-flex">
      <Icon className={`${HEALTH_COLOR[health]} h-3.5 w-3.5`} aria-hidden />
    </span>
  )
}

interface NodeProps {
  node: TreeNode
  depth: number
  itemMilestone: Map<string, string>
  selectedId: string | null
  onSelectItem: (id: string) => void
  // Expand state lives in the persisted store, not per node: every
  // node starts collapsed, which is also what *collapse all* leaves behind.
  expanded: ReadonlySet<string>
  onToggle: (id: string) => void
}

function ItemNode({ node, depth, itemMilestone, selectedId, onSelectItem, expanded, onToggle }: NodeProps) {
  const [confirmDelete, setConfirmDelete] = useState(false)
  const navigate = useNavigate()
  const openNewItem = useDialogs((s) => s.openNewItem)
  const { item, children } = node
  const hasKids = children.length > 0
  const TypeIcon = TYPE_ICON[item.type]
  const StateIcon = STATE_ICON[item.state]
  const milestone = itemMilestone.get(item.id)
  const selected = selectedId === item.id
  const open = expanded.has(item.id)

  return (
    <div>
      <div
        className={
          'group flex items-center gap-1.5 pr-2 py-1 text-sm ' +
          (selected ? 'bg-blue-100 dark:bg-blue-900/40' : 'hover:bg-zinc-100 dark:hover:bg-zinc-900')
        }
        style={{ paddingLeft: `${depth * 14 + 8}px` }}
      >
        {hasKids ? (
          <button
            type="button"
            onClick={() => onToggle(item.id)}
            aria-label={open ? 'Collapse' : 'Expand'}
            className="shrink-0 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200"
          >
            {open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
          </button>
        ) : (
          <span className="w-3.5 shrink-0" />
        )}
        <StateIcon className={`${STATE_COLOR[item.state]} h-3.5 w-3.5 shrink-0`} aria-hidden />
        <TypeIcon className={`${TYPE_COLOR[item.type]} h-3.5 w-3.5 shrink-0`} aria-hidden />
        <RowLink
          to="/projects"
          search={{ id: item.id }}
          onSelect={() => onSelectItem(item.id)}
          className="min-w-0 flex-1 text-left truncate"
        >
          {item.title}
        </RowLink>
        {milestone && (
          <span
            className="shrink-0 inline-flex items-center gap-0.5 rounded bg-cyan-100 dark:bg-cyan-900/40 px-1 text-[10px] text-cyan-700 dark:text-cyan-300"
            title={`Milestone: ${milestone}`}
          >
            <Flag className="h-2.5 w-2.5" aria-hidden />
            {milestone}
          </span>
        )}
        {item.type === 'todo' && (
          <>
            <button
              type="button"
              onClick={() =>
                openNewItem({
                  type: 'todo',
                  parentId: item.id,
                  allowedTypes: ['todo'],
                  stayOnCreate: true,
                })
              }
              title="Add sub-todo"
              aria-label="Add sub-todo"
              className="shrink-0 p-0.5 rounded text-zinc-300 dark:text-zinc-600 opacity-0 group-hover:opacity-100 hover:text-zinc-700 dark:hover:text-zinc-200"
            >
              <Plus className="h-3.5 w-3.5" />
            </button>
            <button
              type="button"
              onClick={() => setConfirmDelete(true)}
              title="Delete todo"
              aria-label="Delete todo"
              className="shrink-0 p-0.5 rounded text-zinc-300 dark:text-zinc-600 opacity-0 group-hover:opacity-100 hover:text-red-600 dark:hover:text-red-400"
            >
              <Trash2 className="h-3.5 w-3.5" />
            </button>
          </>
        )}
        <button
          type="button"
          onClick={() => void navigate({ to: '/items', search: { id: item.id } as never })}
          title="Open in items tree"
          aria-label="Open in items tree"
          className="shrink-0 p-0.5 rounded text-zinc-300 dark:text-zinc-600 opacity-0 group-hover:opacity-100 hover:text-zinc-700 dark:hover:text-zinc-200"
        >
          <ListTree className="h-3.5 w-3.5" />
        </button>
      </div>
      {open &&
        hasKids &&
        children.map((c) => (
          <ItemNode
            key={c.item.id}
            node={c}
            depth={depth + 1}
            itemMilestone={itemMilestone}
            selectedId={selectedId}
            onSelectItem={onSelectItem}
            expanded={expanded}
            onToggle={onToggle}
          />
        ))}
      {confirmDelete && (
        <ItemDeleteDialog
          item={{ id: item.id, shortId: item.shortId, title: item.title }}
          onClose={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}

function DocDragGhost({ path, isFolder }: { path: string; isFolder: boolean }) {
  const Icon = isFolder ? Folder : FileText
  return (
    <DragGhost label={path.split('/').pop() ?? path}>
      <Icon aria-hidden className={`h-4 w-4 shrink-0 ${isFolder ? 'text-amber-500' : 'text-zinc-500'}`} />
    </DragGhost>
  )
}

function MilestoneDragGhost({ title }: { title: string }) {
  return (
    <DragGhost label={title}>
      <Flag className="h-4 w-4 shrink-0 text-cyan-600 dark:text-cyan-400" aria-hidden />
    </DragGhost>
  )
}
