// Grouping + move math for the Projects tree's free-form docs. The API hands us
// a flat list of folder-relative file paths (readme/roadmap/milestones already
// excluded); this turns them into a nested folder tree for display and computes
// the rename/move target when a doc is dragged into a folder or back to root.
//
// Free-form docs are name-sorted (no rank), so drag "before/after" a row does
// not reorder — it only decides the destination *container* (root vs a folder).
// "Inside" a folder node moves the doc into that folder. All moves reduce to the
// project-doc rename/move endpoint (projectsApi.renameDoc).

import { isFolderFormDoc } from './projectDocs'

export interface DocTreeNode {
  // The addressable doc path (flat: `notes.md`; folder form: `specs/readme.md`).
  path: string
  // Display name (flat: `notes.md`; folder: `specs`).
  name: string
  isFolder: boolean
  // The folder that holds this node (`''` = project root).
  container: string
  // Sibling docs/sub-folders under a folder node (empty for a flat doc).
  children: DocTreeNode[]
}

interface Trie {
  files: string[]
  dirs: Map<string, Trie>
}

function normalize(path: string): string {
  return path.trim().replace(/\\/g, '/').replace(/^\/+|\/+$/g, '')
}

function dirOf(path: string): string {
  const i = path.lastIndexOf('/')
  return i < 0 ? '' : path.slice(0, i)
}

function baseOf(path: string): string {
  const i = path.lastIndexOf('/')
  return i < 0 ? path : path.slice(i + 1)
}

// Build the nested folder tree from the flat file list. A folder-form entry
// (`<dir>/readme.md`) becomes a folder node named `<dir>` whose `readme.md` is
// the node itself (path) and whose remaining files/sub-folders are children.
export function buildDocTree(files: string[]): DocTreeNode[] {
  const root: Trie = { files: [], dirs: new Map() }
  for (const raw of files) {
    const p = normalize(raw)
    if (!p) continue
    const segs = p.split('/')
    let node = root
    for (let i = 0; i < segs.length; i++) {
      const seg = segs[i]!
      if (i === segs.length - 1) {
        node.files.push(seg)
      } else {
        let d = node.dirs.get(seg)
        if (!d) {
          d = { files: [], dirs: new Map() }
          node.dirs.set(seg, d)
        }
        node = d
      }
    }
  }
  return toNodes(root, '')
}

function toNodes(trie: Trie, container: string): DocTreeNode[] {
  const nodes: DocTreeNode[] = []
  // Folders first (name-sorted), then flat docs (name-sorted) — mirrors the
  // API's Ordinal sort while keeping containers grouped above their siblings.
  for (const name of [...trie.dirs.keys()].sort()) {
    const sub = trie.dirs.get(name)!
    const folderPath = container ? `${container}/${name}` : name
    const childTrie: Trie = {
      files: sub.files.filter((f) => f.toLowerCase() !== 'readme.md'),
      dirs: sub.dirs,
    }
    nodes.push({
      path: `${folderPath}/readme.md`,
      name,
      isFolder: true,
      container,
      children: toNodes(childTrie, folderPath),
    })
  }
  for (const f of trie.files.slice().sort()) {
    nodes.push({
      path: container ? `${container}/${f}` : f,
      name: f,
      isFolder: false,
      container,
      children: [],
    })
  }
  return nodes
}

// The folder a node receives children into when something is dropped *inside*
// it (`specs/readme.md` → `specs`). Only meaningful for folder nodes.
export function folderContainerOf(node: DocTreeNode): string {
  return dirOf(node.path)
}

// Compute the new path to pass to renameDoc when `dragPath` is moved into
// `container` (`''` = project root). Returns null for a no-op (already there)
// or an illegal move (a folder into itself or its own subtree). For a
// folder-form source the returned path is the destination folder *without*
// `readme.md` — the move endpoint rebuilds `<dest>/readme.md`.
export function docDropTarget(dragPath: string, container: string): string | null {
  const src = normalize(dragPath)
  const dest = normalize(container)

  if (isFolderFormDoc(src)) {
    const ownFolder = dirOf(src) // `specs` or `docs/specs`
    const currentContainer = dirOf(ownFolder)
    if (dest === currentContainer) return null
    // Into itself or a descendant of itself.
    if (dest === ownFolder || dest.startsWith(`${ownFolder}/`)) return null
    const folderName = baseOf(ownFolder)
    return dest ? `${dest}/${folderName}` : folderName
  }

  const currentContainer = dirOf(src)
  if (dest === currentContainer) return null
  const fileName = baseOf(src)
  return dest ? `${dest}/${fileName}` : fileName
}

// The renameDoc target that promotes a flat doc to folder form: `notes.md`
// becomes the folder `notes/` with `notes/readme.md` as its entry.
export function promoteToFolderTarget(flatPath: string): string {
  const p = normalize(flatPath)
  const noMd = p.toLowerCase().endsWith('.md') ? p.slice(0, -3) : p
  return `${noMd}/readme.md`
}
