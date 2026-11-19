import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowRightCircle, FileText, FolderInput, Map as MapIcon, Trash2 } from 'lucide-react'
import { projectsApi, projectsQk, type ProjectDoc } from '../api/projects'
import { MarkdownRender } from './MarkdownRender'
import { BodyEditor } from './BodyEditor'
import { ConflictDialog } from './ConflictDialog'
import { StaleBanner } from './StaleBanner'
import { ProjectDocDeleteDialog } from './ProjectDocDeleteDialog'
import { CopyPathIconButton, CopyRefIconButton } from './CopyButton'
import { CopyShareLinkButton } from './CopyShareLinkButton'
import { useBodyEditorSession } from '../lib/useBodyEditorSession'
import { bodyIsRenderEmpty } from '../lib/markdown'
import { useToasts } from '../stores/toasts'
import { README_DOC, ROADMAP_DOC, isReservedDoc, isFolderFormDoc } from '../lib/projectDocs'
import { bodyRef } from '../lib/resolveLink'
import { promoteToFolderTarget } from '../lib/projectDocTree'
import { editPlaceAt } from '../lib/editPlace'
import { DocOutline } from './DocOutline'

interface Props {
  project: string
  path: string
  onSelectDoc: (project: string, path: string) => void
  onCleared: () => void
}

// Right-pane view + authoring surface for a readme / roadmap / free-form
// project doc: the narrative body (shared BodyEditor, ETag-guarded) plus
// rename / delete for free-form docs. readme/roadmap are body-editable but
// reserved (renamed/deleted via the project + roadmap-scaffold actions, not
// here). There is no separate title field — the body's leading H1 is the title.
export function ProjectDocDetailPanel({ project, path, onSelectDoc, onCleared }: Props) {
  const q = useQuery({
    queryKey: projectsQk.doc(project, path),
    queryFn: () => projectsApi.doc(project, path),
    retry: false,
  })

  if (q.isLoading) return <div className="p-4 text-sm text-zinc-500">Loading…</div>
  if (q.error || !q.data) {
    return (
      <div className="p-4 text-sm text-red-600 dark:text-red-400">
        Document not found: {project}/{path}
      </div>
    )
  }

  return (
    <DocBody key={`${project}:${path}`} doc={q.data} onSelectDoc={onSelectDoc} onCleared={onCleared} />
  )
}

function DocBody({
  doc,
  onSelectDoc,
  onCleared,
}: {
  doc: ProjectDoc
  onSelectDoc: (project: string, path: string) => void
  onCleared: () => void
}) {
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)
  const { project, path } = doc
  const [confirmDelete, setConfirmDelete] = useState(false)
  const reserved = isReservedDoc(path)
  const isRoadmap = path.toLowerCase() === ROADMAP_DOC

  const session = useBodyEditorSession({
    fetchWithEtag: async () => {
      const { doc: fresh, etag } = await projectsApi.fetchDocWithEtag(project, path)
      return { body: fresh.body, etag }
    },
    patch: (body, ifMatch) => projectsApi.patchDoc(project, path, body, ifMatch).then(() => {}),
    // ['projects'] prefix (not just the doc) so a body edit that retitles via
    // the leading H1 also relabels the tree row.
    onSaved: () => qc.invalidateQueries({ queryKey: ['projects'] }),
    onReload: () => qc.invalidateQueries({ queryKey: projectsQk.doc(project, path) }),
    toast: push,
    currentBody: doc.body,
  })

  const rename = async () => {
    const next = window.prompt(
      `Rename ${project}/${path}.\n\nLinks to it in other docs are repointed.\n\nNew path (project-folder-relative):`,
      path,
    )
    if (!next || next.trim() === '' || next.trim() === path) return
    try {
      const r = await projectsApi.renameDoc(project, path, next.trim())
      push('success', `Renamed → ${r.project}/${r.newPath}`)
      void qc.invalidateQueries({ queryKey: ['projects'] })
      onSelectDoc(r.project, r.newPath)
    } catch (e) {
      push('error', `Rename failed: ${String(e)}`)
    }
  }

  // Promote a flat free-form doc to folder form (`notes.md` → `notes/readme.md`)
  // so it can hold sub-docs. A plain move; the body travels with it.
  const convertToFolder = async () => {
    try {
      const r = await projectsApi.renameDoc(project, path, promoteToFolderTarget(path))
      push('success', `Converted to folder → ${r.project}/${r.newPath}`)
      void qc.invalidateQueries({ queryKey: ['projects'] })
      onSelectDoc(r.project, r.newPath)
    } catch (e) {
      push('error', `Convert failed: ${String(e)}`)
    }
  }

  const Icon = isRoadmap ? MapIcon : FileText

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
        <div className="flex items-center gap-2">
          <Icon className="h-4 w-4 text-zinc-500 shrink-0" aria-hidden />
          <span className="font-mono text-sm text-zinc-700 dark:text-zinc-200 truncate">{path}</span>
          {path === README_DOC && (
            <CopyRefIconButton value={bodyRef(`project:${project}`)} title="Copy ref for body text" />
          )}
          <CopyPathIconButton value={doc.filePath} title="Copy file path" />
          <CopyShareLinkButton path={doc.filePath} />
          {reserved && (
            <span className="text-[10px] uppercase tracking-wide text-zinc-400" title="Recognized project doc — renamed/deleted via the project, not here">
              recognized
            </span>
          )}
          {!reserved && (
            <div className="ml-auto flex items-center gap-1 shrink-0">
              {!isFolderFormDoc(path) && (
                <button
                  type="button"
                  onClick={convertToFolder}
                  title="Convert to folder (allows sub-docs)"
                  aria-label="Convert to folder"
                  className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300"
                >
                  <FolderInput className="h-4 w-4" />
                </button>
              )}
              <button
                type="button"
                onClick={rename}
                title="Rename / move (links to it are repointed)"
                aria-label="Rename"
                className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300"
              >
                <ArrowRightCircle className="h-4 w-4" />
              </button>
              <button
                type="button"
                onClick={() => setConfirmDelete(true)}
                title="Delete document"
                aria-label="Delete"
                className="p-1.5 rounded text-zinc-600 hover:bg-red-100 hover:text-red-700 dark:hover:bg-red-900/40 dark:text-zinc-300"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </div>
          )}
        </div>
      </div>

      <div className="flex-1 min-h-0 overflow-auto p-4 flex flex-col gap-4">
        <section className="group relative flex flex-col">
          <DocOutline body={doc.body ?? ''} docRef={doc.path === README_DOC ? `project:${doc.project}` : `.knit/projects/${doc.project}/${doc.path}`} enabled={!session.editing} onEdit={session.editing ? undefined : () => session.open()}>
            {session.editing ? (
              <>
                {session.staleActive && (
                  <StaleBanner onReload={() => void session.reloadStale()} onKeepMine={session.dismissStale} />
                )}
                <BodyEditor
                  initialValue={session.seedBody ?? doc.body}
                  onSave={session.save}
                  onCancel={session.cancel}
                  outline={{ docRef: doc.path === README_DOC ? `project:${doc.project}` : `.knit/projects/${doc.project}/${doc.path}` }}
                  initialPlace={session.openPlace}
                  busy={session.busy}
                  onDirtyChange={session.setEditorDirty}
                  reseedToken={session.reseedToken}
                />
              </>
            ) : (
              <div onDoubleClick={(e) => session.open(editPlaceAt(e.currentTarget, e.target, doc.body ?? ''))}>
                {bodyIsRenderEmpty(doc.body) ? (
                  <p className="text-sm italic text-zinc-500">No body content. Double-click to start writing.</p>
                ) : (
                  <MarkdownRender
                    landing={session.landing}
                    onLanded={session.clearLanding}
                    body={doc.body}
                    docRef={doc.path === README_DOC ? `project:${doc.project}` : `.knit/projects/${doc.project}/${doc.path}`}
                    linkParent={`.knit/projects/${doc.project}/${doc.path}`.replace(/\/[^/]*$/, '')}
                    trackSection
                    onEditBody={session.quickSave}
                  />
                )}
              </div>
            )}
          </DocOutline>
        </section>
      </div>

      {session.conflictActive && (
        <ConflictDialog
          shortId={`${project}/${path}`}
          busy={session.busy}
          onReload={session.reload}
          onOverwrite={() => void session.overwrite()}
          onKeepEditing={session.keepEditing}
        />
      )}
      {confirmDelete && (
        <ProjectDocDeleteDialog
          project={project}
          path={path}
          onClose={() => setConfirmDelete(false)}
          onDeleted={() => {
            setConfirmDelete(false)
            onCleared()
          }}
        />
      )}
    </div>
  )
}
