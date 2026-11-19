// Share links. A pasted forge URL or `<repo_name>:<path>` resolves
// by its `.knit/` segment, never by parsing the forge's URL shape — so
// every forge and a hand-written link work alike. When in doubt, stop.

export const FORGE_KINDS = ['github', 'gitlab', 'gitea', 'azure'] as const

export interface PasteOrigin {
  /** This repo's web address, or null when it has no forge origin. */
  web: string | null
  repoName: string
}

export type PasteResult =
  | { ok: true; path: string }
  | { ok: false; error: string }

const SEGMENT = /(?:^|[/=:])(\.knit\/[^?#&\s]*)/g

/** Whether the text is a pasted link for {@link resolvePastedLink}: it holds `.knit/` once decoded and is not a plain ref. */
export function isPastedLink(text: string): boolean {
  const s = text.trim()
  if (s.startsWith('.knit/')) return false
  const decoded = safeDecode(s)
  return (decoded ?? s).includes('.knit/')
}

/**
 * The `.knit/…` path a pasted link names, for the jump dialog's resolver —
 * which takes an item file by the id in its name, so a link from before a
 * retitle still lands. Only this repo's links: a URL must start with its
 * `{web}` (decoded, case-insensitive), a path form must carry its name.
 */
export function resolvePastedLink(text: string, origin: PasteOrigin): PasteResult {
  const s = text.trim()
  const decoded = safeDecode(s)
  if (decoded === null) return { ok: false, error: 'The link does not decode' }
  const segments = [...decoded.matchAll(SEGMENT)].map((m) => m[1]!)
  if (segments.length === 0) return { ok: false, error: 'No .knit/ path in the link' }
  if (segments.length > 1) return { ok: false, error: 'The link holds more than one .knit/ path' }
  const path = segments[0]!.replace(/\/+$/, '')

  if (/^https?:\/\//i.test(decoded)) {
    const web = origin.web === null ? null : (safeDecode(origin.web) ?? origin.web)
    if (web === null) return { ok: false, error: 'Link from another repo — this repo has no forge origin to match it' }
    const head = decoded.slice(0, web.length)
    const next = decoded.charAt(web.length)
    if (head.toLowerCase() !== web.toLowerCase() || !['/', '?', ''].includes(next))
      return { ok: false, error: 'Link from another repo' }
    return { ok: true, path }
  }

  const form = /^([^\s:/]+):(?:\.?\/)?(.*)$/.exec(decoded)
  if (form) {
    if (form[1] !== origin.repoName) return { ok: false, error: `Link from another repo (${form[1]})` }
    return { ok: true, path }
  }
  return { ok: true, path }
}

function safeDecode(s: string): string | null {
  try {
    return decodeURIComponent(s)
  } catch {
    return null
  }
}

/** The command per forge kind an unknown host's popover offers. */
export function forgeCommands(host: string): { label: string; command: string }[] {
  return FORGE_KINDS.map((kind) => ({ label: kind, command: `knit config set forges.${host} ${kind}` }))
}
