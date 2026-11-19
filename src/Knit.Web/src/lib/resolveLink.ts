// Resolves relative hrefs from markdown bodies into one of four kinds:
//   * kb entry  → bucket + slug (drives /kb?bucket=&slug=)
//   * kb folder → subpath (drives /kb?folder=)
//   * item      → item id (drives /items?id=)
//   * design    → design slug (drives /designs?slug=)
//   * project   → project slug + doc (drives /projects?doc=<slug>/<path>)
//   * milestone → project + slug (drives /projects?mp=&ms=)
//   * file      → repo-relative path (drives /file?path=)
//   * ignore    → caller falls back to clipboard copy
//
// Callers pass `parent`, the directory of the markdown file expressed
// repo-relative in posix form (e.g. `.knit/kb/features/assembly-line`
// or `.knit/items`). Hrefs with a protocol, leading `/`, `#`, or
// `mailto:` are filtered out earlier by MarkdownRender's isInternalHref;
// this resolver still rejects them defensively.
//
// A `#<anchor>` on a ref, a `.knit/…` path or a relative link rides along as
// `section`; the body view scrolls to that heading. A diagram pointer
// fragment (`#mermaid-N…`) is not a section and is dropped.

export type KbBucket = 'adr' | 'doc' | 'feature' | 'log' | 'idea' | 'source'

export type ResolvedLink = (
  | { kind: 'kb-entry'; bucket: KbBucket; slug: string }
  | { kind: 'kb-folder'; subpath: string }
  | { kind: 'item'; id: string }
  | { kind: 'design'; slug: string }
  | { kind: 'project'; slug: string; path?: string }
  | { kind: 'milestone'; project: string; slug: string }
  | { kind: 'file'; path: string }
  | { kind: 'ignore' }
) & { section?: string }

const BUCKET_BY_FOLDER: Record<string, KbBucket> = {
  adrs: 'adr',
  docs: 'doc',
  features: 'feature',
  logs: 'log',
  ideas: 'idea',
  sources: 'source',
}

// Canonical `bucket:slug` ref, matched whole. The front-end form of the
// C# Ref grammar; shared by the marked code-span linkifier (markdown.ts)
// and jump-to-ref. resolveLink owns it because it lands first.
export const KB_REF_RE = /^(adr|doc|feature|log|idea|source):([a-z0-9][a-z0-9/-]*)$/

// Designs sit in their own address space (`.knit/designs/`), not under kb/,
// so they get a dedicated ref scheme rather than a KbBucket. Slug is the
// filename-derived design slug (mirrors the C# `design:` bucket).
export const DESIGN_REF_RE = /^design:([a-z0-9][a-z0-9/-]*)$/

// `project:<slug>` names a project doc folder and opens its readme; the
// slug is a single kebab segment (mirrors the C# `project:` bucket).
export const PROJECT_REF_RE = /^project:([a-z0-9][a-z0-9-]*)$/

// `item:<id>` with the full 18-char NanoID only — a prefix is frozen text
// that turns ambiguous as the repo grows, so it stays a plain code-span
// and `knit verify` flags it.
export const ITEM_REF_RE = /^item:([0-9A-Za-z]{18})$/

// A doc without a ref (a milestone, a non-readme project doc) is addressed by
// its path from the knit root (mirrors the C# DocRef path form).
export const KNIT_PATH_REF_RE = /^\.knit\/(?:[^/#\s\\]+\/)*[^/#\s\\]+\.md$/

// A `#mermaid-<n>` or `#<file>.svg` fragment is a diagram pointer, never a
// heading anchor (an anchor holds no `.`).
const POINTER_FRAGMENT_RE = /^(?:mermaid-\d|[^/]+\.svg(?:\/|$))/i

/** Splits `doc#fragment`; a pointer fragment yields no section. */
export function splitSection(href: string): { doc: string; section?: string } {
  const hash = href.indexOf('#')
  if (hash < 0) return { doc: href }
  const fragment = href.slice(hash + 1)
  const doc = href.slice(0, hash)
  return !fragment || POINTER_FRAGMENT_RE.test(fragment) ? { doc } : { doc, section: fragment }
}

/** Whether `target` is a Knit ref or `.knit/…` path, a section or pointer fragment allowed. */
export function isDocRef(target: string): boolean {
  const { doc } = splitSection(target)
  return [KB_REF_RE, DESIGN_REF_RE, PROJECT_REF_RE, ITEM_REF_RE, KNIT_PATH_REF_RE].some((re) => re.test(doc))
}

// The cited form for body text: the ref as a code-span, which the renderer
// linkifies. An item adds its quoted title — the id alone tells the reader
// nothing; a KB / design / project slug already reads.
export function bodyRef(ref: string, title?: string): string {
  return title ? `\`${ref}\` "${title}"` : `\`${ref}\``
}

const KB_ROOT = '.knit/kb'
const DESIGNS_ROOT = '.knit/designs'
const ITEMS_ROOT = '.knit/items'
const PROJECTS_ROOT = '.knit/projects'
// An item is a flat `<id>-<slug>.md`; the NanoID has no hyphen.
const ITEM_FILE_ID_RE = /^([0-9A-Za-z]{18})(?=[-.])[^/]*$/

export function joinPosix(base: string, rel: string): string {
  const stack: string[] = []
  for (const seg of (base + '/' + rel).split('/')) {
    if (seg === '' || seg === '.') continue
    if (seg === '..') stack.pop()
    else stack.push(seg)
  }
  return stack.join('/')
}

export function resolveLink(parent: string, href: string): ResolvedLink {
  if (!href) return { kind: 'ignore' }
  if (href.startsWith('#')) return { kind: 'ignore' }
  const { doc, section } = splitSection(href)
  const t = resolveDoc(parent, doc)
  return section && t.kind !== 'ignore' ? { ...t, section } : t
}

function resolveDoc(parent: string, href: string): ResolvedLink {
  if (!href) return { kind: 'ignore' }
  // A `bucket:slug` ref (from a linkified code-span's data-ref, or a
  // pasted ref in jump-to-ref) resolves straight to a KB entry — no path math, so
  // the flat-vs-folder-form ambiguity disappears. Must precede the
  // protocol guard below, which would otherwise reject the `adr:` prefix.
  const designRef = DESIGN_REF_RE.exec(href)
  if (designRef) return { kind: 'design', slug: designRef[1]! }
  const projectRef = PROJECT_REF_RE.exec(href)
  if (projectRef) return { kind: 'project', slug: projectRef[1]! }
  const itemRef = ITEM_REF_RE.exec(href)
  if (itemRef) return { kind: 'item', id: itemRef[1]!.toUpperCase() }
  const ref = KB_REF_RE.exec(href)
  if (ref) return { kind: 'kb-entry', bucket: ref[1] as KbBucket, slug: ref[2]! }
  if (/^[a-z][a-z0-9+.-]*:/i.test(href)) return { kind: 'ignore' }
  if (href.startsWith('/')) return { kind: 'ignore' }

  const stripped = href.split('?')[0]!
  const full = joinPosix(KNIT_PATH_REF_RE.test(stripped) ? '' : parent, stripped)
  if (full === '') return { kind: 'kb-folder', subpath: '' }

  // An item file (flat, directly in .knit/items) → deep-link to item route.
  if (full === ITEMS_ROOT) return { kind: 'file', path: full }
  if (full.startsWith(ITEMS_ROOT + '/') && full.toLowerCase().endsWith('.md')) {
    const tail = full.slice(ITEMS_ROOT.length + 1)
    const match = ITEM_FILE_ID_RE.exec(tail)
    if (match) return { kind: 'item', id: match[1]!.toUpperCase() }
    return { kind: 'file', path: full }
  }

  // Inside .knit/designs: a `<slug>-design.md` at the root, or any doc
  // under `completed/<slug>/`, deep-links to the design route instead of
  // the raw file viewer. Other files (e.g. attachments) fall through.
  if (full.startsWith(DESIGNS_ROOT + '/')) {
    const parts = full.slice(DESIGNS_ROOT.length + 1).split('/')
    if (parts[0] === 'completed') {
      if (parts.length >= 2 && parts[1]) return { kind: 'design', slug: parts[1] }
      return { kind: 'file', path: full }
    }
    if (parts.length === 1 && /-design\.md$/i.test(parts[0]!)) {
      return { kind: 'design', slug: parts[0]!.replace(/-design\.md$/i, '') }
    }
    return { kind: 'file', path: full }
  }

  // Inside .knit/projects: a milestone opens its panel, any other doc the
  // project doc panel.
  if (full.startsWith(PROJECTS_ROOT + '/') && full.toLowerCase().endsWith('.md')) {
    const parts = full.slice(PROJECTS_ROOT.length + 1).split('/')
    if (parts.length === 3 && parts[1] === 'milestones') {
      return { kind: 'milestone', project: parts[0]!, slug: parts[2]!.replace(/\.md$/i, '') }
    }
    if (parts.length >= 2) return { kind: 'project', slug: parts[0]!, path: parts.slice(1).join('/') }
  }

  // Inside .knit/kb: try to map onto bucket/entry/folder shape.
  if (full === KB_ROOT) return { kind: 'kb-folder', subpath: '' }
  if (full.startsWith(KB_ROOT + '/')) {
    const inKb = full.slice(KB_ROOT.length + 1)
    const parts = inKb.split('/')
    const head = parts[0]!
    const bucket = BUCKET_BY_FOLDER[head]
    const isMd = inKb.toLowerCase().endsWith('.md')

    if (!bucket) {
      // KB-level non-bucket file (e.g. images, attached PDFs). Markdown
      // siblings like kb-index.md are technically renderable but they're
      // generated TOCs — let the file viewer show them raw.
      if (!isMd) return { kind: 'file', path: full }
      return { kind: 'kb-folder', subpath: parts.slice(0, -1).join('/') }
    }
    if (!isMd) {
      // Pointing at a bucket folder or sub-folder — open the folder card.
      return { kind: 'kb-folder', subpath: inKb }
    }
    const file = parts[parts.length - 1]!.toLowerCase()
    if (file === 'index.md') {
      return { kind: 'kb-folder', subpath: parts.slice(0, -1).join('/') }
    }
    const inner = parts.slice(1)
    if (inner.length === 0) return { kind: 'ignore' }
    let slug: string
    if (inner.length === 1) {
      slug = inner[0]!.replace(/\.md$/i, '')
    } else if (inner[inner.length - 1]!.toLowerCase() === 'readme.md') {
      slug = inner.slice(0, -1).join('/')
    } else {
      slug = inner.join('/').replace(/\.md$/i, '')
    }
    if (!slug) return { kind: 'ignore' }
    return { kind: 'kb-entry', bucket, slug }
  }

  // Anywhere else under the repo root — code file, image, doc. The
  // server-side viewer enforces containment, so we don't double-check
  // here; this lets us forward odd paths (e.g. /design/foo.md) verbatim.
  return { kind: 'file', path: full }
}

// The SPA route a resolved link opens. A design's tab is unknown here, so the
// designs route opens the active tab.
export function navTarget(t: ResolvedLink): { to: string; search: Record<string, string>; hash?: string } | null {
  const target = docTarget(t)
  return target && t.section ? { ...target, hash: t.section } : target
}

function docTarget(t: ResolvedLink): { to: string; search: Record<string, string> } | null {
  switch (t.kind) {
    case 'kb-entry':
      return { to: '/kb', search: { bucket: t.bucket, slug: t.slug } }
    case 'kb-folder':
      return { to: '/kb', search: { folder: t.subpath } }
    case 'item':
      return { to: '/items', search: { id: t.id } }
    case 'design':
      return { to: '/designs', search: { slug: t.slug } }
    case 'project':
      return { to: '/projects', search: { doc: `${t.slug}/${t.path ?? 'readme.md'}` } }
    case 'milestone':
      return { to: '/projects', search: { mp: t.project, ms: t.slug } }
    case 'file':
      return { to: '/file', search: { path: t.path } }
    default:
      return null
  }
}

// Derives the repo-relative parent directory of an absolute file path
// that lives under the repo root. Used by KB entry pages that have the
// entry's filePath (absolute) but no separate "parent subpath" string.
export function parentFromAbsoluteUnderRepo(absPath: string, repoMarker: string): string | null {
  const norm = absPath.replace(/\\/g, '/')
  const i = norm.toLowerCase().indexOf('/' + repoMarker.toLowerCase() + '/')
  if (i < 0) return null
  const fromMarker = norm.slice(i + 1)
  const parts = fromMarker.split('/').slice(0, -1)
  return parts.join('/')
}
