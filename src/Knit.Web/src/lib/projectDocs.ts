// Classification shared by the Projects tree + doc detail panel. readme.md and
// roadmap.md are *recognized* project docs: body-editable like any doc, but not
// renamable/deletable as free-form — they belong to the project's scaffold +
// lifecycle (delete the project, or use the roadmap scaffold action). Keep in
// sync with ProjectDoc.IsReserved / ProjectsLayout in Knit.Core.

export const README_DOC = 'readme.md'
export const ROADMAP_DOC = 'roadmap.md'

export function isReservedDoc(path: string): boolean {
  const p = path.trim().replace(/\\/g, '/').toLowerCase()
  return p === README_DOC || p === ROADMAP_DOC
}

// A free-form doc is in folder form when it's addressed by its folder's
// readme (`<name>/readme.md`) — rename/delete then act on the whole folder.
export function isFolderFormDoc(path: string): boolean {
  return path.replace(/\\/g, '/').toLowerCase().endsWith('/' + README_DOC)
}

// The folder name owning a folder-form doc (`guide/readme.md` → `guide`), or
// null for a flat doc. Used to phrase rename/delete prompts.
export function docFolderName(path: string): string | null {
  const norm = path.replace(/\\/g, '/')
  if (!isFolderFormDoc(norm)) return null
  return norm.slice(0, norm.length - (README_DOC.length + 1))
}
