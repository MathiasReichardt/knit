import type { SearchHit } from '../api/client'

export interface NavTarget {
  to: string
  search: Record<string, string>
}

// The route a search hit opens. A design's tab is left to the designs route,
// which reads it off the entry.
export function searchHitTarget(hit: Pick<SearchHit, 'kind' | 'id'>): NavTarget | null {
  switch (hit.kind) {
    case 'item':
      return { to: '/items', search: { id: hit.id } }
    case 'design':
      return { to: '/designs', search: { slug: hit.id } }
    case 'kb': {
      // The ref "bucket:slug"; the slug may contain '/' but no ':'.
      const idx = hit.id.indexOf(':')
      if (idx <= 0) return null
      return { to: '/kb', search: { bucket: hit.id.slice(0, idx), slug: hit.id.slice(idx + 1) } }
    }
    case 'project': {
      // "<project>/<rel>" (rel folder-relative, .md stripped), matching the SSE
      // id. A milestones/ rel opens the milestone panel; any other rel is a
      // readme/roadmap/free-form doc (re-add the .md).
      const slash = hit.id.indexOf('/')
      if (slash <= 0) return null
      const project = hit.id.slice(0, slash)
      const rel = hit.id.slice(slash + 1)
      const mPrefix = 'milestones/'
      return rel.startsWith(mPrefix)
        ? { to: '/projects', search: { mp: project, ms: rel.slice(mPrefix.length) } }
        : { to: '/projects', search: { doc: `${project}/${rel}.md` } }
    }
  }
}

// Ctrl+Enter (Cmd+Enter on macOS) in a result list: open the pick in a new tab.
export function isNewTabKey(e: { key: string; ctrlKey: boolean; metaKey: boolean }): boolean {
  return e.key === 'Enter' && (e.ctrlKey || e.metaKey)
}
