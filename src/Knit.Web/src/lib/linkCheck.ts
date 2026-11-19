// Probes whether a resolved link target actually exists before the
// caller navigates. Returns `true` if the target resolves (HTTP 2xx),
// `false` for 404, and rethrows on other errors so unrelated failures
// (network, CORS, server crash) don't get silently swallowed as
// "broken link" toasts.
//
// One extra round-trip per link click — acceptable for loopback-only
// dev/local server, and lets the SPA refuse to navigate into a 404
// state.

import { navTarget, type ResolvedLink } from './resolveLink'
import { useItemReveal } from '../stores/itemReveal'

export interface FollowLinkDeps {
  navigate: (to: { to: string; search?: unknown; hash?: string }) => void | Promise<unknown>
  toast: (kind: 'success' | 'error' | 'info', message: string) => void
  // Lets the items pane respond to a sibling-item link without a route
  // hop. Other callers can leave this undefined; the helper falls back
  // to a /items navigation.
  onItem?: (id: string) => void
}

// Probes the target; if it resolves, performs the navigation. Otherwise
// fires a toast and stays put. Use this in onInternalLink handlers so
// the user gets immediate feedback when a markdown link is stale.
export async function followLink(
  t: ResolvedLink,
  href: string,
  deps: FollowLinkDeps,
): Promise<void> {
  if (t.kind === 'ignore') {
    void navigator.clipboard?.writeText(href)
    deps.toast('info', `Copied path: ${href}`)
    return
  }
  // Designs carry a tab dimension (active vs completed). One GET both
  // probes existence and reads `completed`, so we open the right tab and
  // the tree can reveal the row — the boolean checkLinkExists probe can't
  // surface that. Mirrors JumpToRef's design branch.
  if (t.kind === 'design') {
    let completed: boolean
    try {
      const r = await fetch(`/api/designs/${encodeURIComponent(t.slug)}`)
      if (r.status === 404) {
        deps.toast('error', `Broken link: ${describe(t, href)}`)
        return
      }
      if (!r.ok) throw new Error(`HTTP ${r.status}`)
      completed = ((await r.json()) as { completed?: boolean }).completed ?? false
    } catch (e) {
      deps.toast('error', `Failed to check link: ${e instanceof Error ? e.message : String(e)}`)
      return
    }
    await deps.navigate({
      to: '/designs',
      search: { tab: completed ? 'completed' : 'active', slug: t.slug },
      hash: t.section,
    })
    return
  }
  let exists: boolean
  try {
    exists = await checkLinkExists(t)
  } catch (e) {
    deps.toast('error', `Failed to check link: ${e instanceof Error ? e.message : String(e)}`)
    return
  }
  if (!exists) {
    deps.toast('error', `Broken link: ${describe(t, href)}`)
    return
  }
  if (t.kind === 'item') {
    useItemReveal.getState().request(t.id)
    // The in-pane item switch has no URL hop to carry a section.
    if (deps.onItem && !t.section) {
      deps.onItem(t.id)
      return
    }
  }
  const target = navTarget(t)
  if (target) await deps.navigate(target)
}

function describe(t: ResolvedLink, href: string): string {
  switch (t.kind) {
    case 'kb-entry':
      return `${t.bucket}:${t.slug} not found`
    case 'kb-folder':
      return `KB folder "${t.subpath || '(root)'}" not found`
    case 'item':
      return `item ${t.id} not found`
    case 'design':
      return `design:${t.slug} not found`
    case 'project':
      return t.path && t.path !== 'readme.md' ? `project doc ${t.slug}/${t.path} not found` : `project:${t.slug} not found`
    case 'milestone':
      return `milestone ${t.project}/${t.slug} not found`
    case 'file':
      return `file "${t.path}" not found`
    default:
      return href
  }
}

export async function checkLinkExists(t: ResolvedLink): Promise<boolean> {
  let url: string
  switch (t.kind) {
    case 'kb-entry':
      url = `/api/kb/entry/${encodeURIComponent(t.bucket)}/${t.slug
        .split('/')
        .map(encodeURIComponent)
        .join('/')}`
      break
    case 'kb-folder': {
      const sub = t.subpath
        .split('/')
        .filter(Boolean)
        .map(encodeURIComponent)
        .join('/')
      url = `/api/kb/folder/${sub}`
      break
    }
    case 'item':
      url = `/api/items/${encodeURIComponent(t.id)}`
      break
    case 'design':
      url = `/api/designs/${encodeURIComponent(t.slug)}`
      break
    case 'project':
      url = `/api/projects/${encodeURIComponent(t.slug)}/docs/${(t.path ?? 'readme.md').split('/').map(encodeURIComponent).join('/')}`
      break
    case 'milestone':
      url = `/api/projects/${encodeURIComponent(t.project)}/milestones/${encodeURIComponent(t.slug)}`
      break
    case 'file':
      url = `/api/file?path=${encodeURIComponent(t.path)}`
      break
    default:
      return true
  }
  const r = await fetch(url)
  if (r.ok) return true
  if (r.status === 404) return false
  throw new Error(`HTTP ${r.status} probing ${url}`)
}
