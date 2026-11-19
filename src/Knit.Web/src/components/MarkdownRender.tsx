import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useRouter, useRouterState } from '@tanstack/react-router'
import { RAW_IMAGE_ATTR, renderMarkdown, withRawFiles, withSpaHrefs } from '../lib/markdown'
import { isSectionLinkClick, replaceUrlHash, sectionLinkAt } from '../lib/sectionLink'
import { isDocRef, joinPosix, navTarget, resolveLink } from '../lib/resolveLink'
import { HeadingSlugger } from '../lib/outline'
import { GfmSlugger, withGfmHeadingIds } from '../lib/gfmSlug'
import { toggleTaskListItem } from '../lib/taskList'
import { useResolvedTheme } from '../stores/theme'
import { useToasts } from '../stores/toasts'
import { notProseSizeClass, proseSizeClass, useMarkdownSize } from '../stores/markdownSize'
import { replaceFence, splitChunks } from '../mermaid/fence'
import { MermaidBlock } from '../mermaid/MermaidBlock'
import { SvgFileBlock } from './SvgFileBlock'
import { ImageOverlay } from './ImageOverlay'
import { mermaidThemeVariables } from '../lib/mermaidTheme'
import { activeHeadingId, holdHeading, holdLine, landingReady, scrollParent, type Landing } from '../lib/editPlace'
import { flashIn } from '../lib/headingFlash'

interface MarkdownRenderProps {
  body: string
  /**
   * If provided, left-clicks on links whose href is relative (no protocol,
   * no leading `/`, no `#`) are intercepted: the default browser
   * navigation is cancelled and the callback receives the raw href. Use
   * this to route in-doc links (e.g. `features/foo/readme.md`) through
   * the SPA router instead of letting the browser hit the dev server
   * with a phantom path.
   */
  onInternalLink?: (href: string) => void
  /**
   * Saves an in-place edit of the rendered body: a GFM task-list checkbox
   * click (the clicked box is mapped to the Nth `- [ ]` / `- [x]` token in
   * `body` and flipped) or a mermaid diagram's layout menu (the fence's
   * frontmatter rewritten). MarkdownRender stays host-agnostic — each host
   * wires its own save mutation (item vs KB). Without this prop the
   * checkboxes still render but toggling is a no-op, and diagrams get no
   * layout menu.
   */
  onEditBody?: (newBody: string) => void | Promise<void>
  /**
   * The frontmatter title for this body. When supplied, the leading `# H1`
   * is stripped only if it mirrors the title;
   * a drifted H1 stays visible. Omit it where there's no title context.
   */
  title?: string
  /**
   * The body's canonical ref (`item:<id>`, `design:<slug>`, …) or, where it
   * has none, its repo-relative `.knit/…` path. Clicking a mermaid diagram
   * copies a pointer to the clicked element, prefixed with this; each h2/h3
   * gets a hover button copying `<docRef>#<anchor>`.
   */
  docRef?: string
  /**
   * The page's main body: lands on the URL's `#<anchor>` heading (a followed
   * section ref, a reload) and keeps the heading being read in the URL.
   */
  trackSection?: boolean
  /**
   * The repo-relative directory of the markdown file. When supplied, relative
   * links get a real SPA href (new tab via middle-click / Ctrl+click), images
   * and PDF links are served from the repo, and an image link to an SVG next to
   * the doc draws as a diagram; refs get an href regardless, since they need no
   * directory.
   */
  linkParent?: string
  /**
   * Where to scroll once this body renders (the editor's cursor after save /
   * cancel). Held briefly while diagrams render; `onLanded` clears it.
   */
  landing?: Landing | null
  onLanded?: () => void
  /**
   * Give every heading the forge's anchor (github-slugger) instead of Knit's
   * section anchor: for a page written to be read on a forge, whose own
   * `#anchor` links use that rule.
   */
  forgeAnchors?: boolean
}

// A landing waiting for the saved text gives up after this long (the doc
// moved on remotely, or the server normalized it).
const LANDING_WAIT_MS = 3000

// The section the reader scrolled to, per history entry and doc. A body shown
// again on the same entry (the editor closed) with that hash is no arrival
// there: it isn't flashed. A followed link or a reload is a new entry or a
// fresh page, and lands as usual.
const readSections = new Map<string, string>()
const entryKey = (docRef: string | undefined) =>
  `${(window.history.state as { __TSR_key?: string } | null)?.__TSR_key ?? ''}|${docRef ?? ''}`

function isInternalHref(href: string): boolean {
  if (!href) return false
  if (href.startsWith('#')) return false
  if (href.startsWith('/')) return false
  if (href.startsWith('mailto:')) return false
  return !/^[a-z][a-z0-9+.-]*:/i.test(href)
}

export function MarkdownRender({
  body,
  onInternalLink,
  onEditBody,
  title,
  docRef,
  linkParent,
  landing,
  onLanded,
  trackSection = false,
  forgeAnchors = false,
}: MarkdownRenderProps) {
  const router = useRouter()
  const toast = useToasts((s) => s.push)
  const size = useMarkdownSize((s) => s.size)
  const theme = useResolvedTheme()
  const themeVariables = useMemo(() => {
    const font = getComputedStyle(document.body).fontFamily
    return { light: mermaidThemeVariables('light', font), dark: mermaidThemeVariables('dark', font) }
  }, [])
  const chunks = useMemo(() => splitChunks(body ?? ''), [body])
  // One slugger per body so heading anchor ids stay deduped across mermaid
  // chunks and line up with extractOutline. Computed in a memo (not inline)
  // so re-renders of the same body don't re-run the stateful slugger.
  const targetOf = useCallback(
    (raw: string, isRef: boolean) =>
      isRef || (linkParent !== undefined && isInternalHref(raw))
        ? navTarget(resolveLink(linkParent ?? '', raw))
        : null,
    [linkParent],
  )
  const htmls = useMemo(() => {
    const slugger = new HeadingSlugger()
    const forge = forgeAnchors ? new GfmSlugger() : null
    const spaHref = (raw: string, isRef: boolean) => {
      const t = targetOf(raw, isRef)
      return t ? router.buildLocation(t as never).href : null
    }
    const rawUrl = (raw: string) => {
      if (linkParent === undefined || !isInternalHref(raw)) return null
      try {
        return `/api/raw?path=${encodeURIComponent(joinPosix(linkParent, decodeURIComponent(raw.split(/[?#]/)[0]!)))}`
      } catch {
        return null
      }
    }
    return chunks.map((c, i) => {
      if (c.kind !== 'md') return null
      const html = withSpaHrefs(
        withRawFiles(
          renderMarkdown(c.text, slugger, title, { firstLine: c.line, leading: i === 0, sectionLinks: !!docRef }),
          rawUrl,
        ),
        spaHref,
      )
      return forge ? withGfmHeadingIds(html, forge) : html
    })
  }, [chunks, title, targetOf, router, docRef, linkParent, forgeAnchors])
  const fences = useMemo(() => {
    let n = 0
    return chunks.map((c) => (c.kind === 'mermaid' ? ++n : 0))
  }, [chunks])
  const [zoomed, setZoomed] = useState<{ src: string; alt: string } | null>(null)
  const closeZoomed = useCallback(() => setZoomed(null), [])
  const rootRef = useRef<HTMLDivElement>(null)
  const onLandedRef = useRef(onLanded)
  useEffect(() => {
    onLandedRef.current = onLanded
  })
  useEffect(() => {
    const root = rootRef.current
    if (!landing || !root) return
    const done = () => onLandedRef.current?.()
    if (!landingReady(landing, body ?? '')) {
      const timer = setTimeout(done, LANDING_WAIT_MS)
      return () => clearTimeout(timer)
    }
    return holdLine(root, landing, done)
  }, [landing, body])
  const hash = useRouterState({ select: (s) => s.location.hash })
  // Lands once per doc + anchor: a refetch re-rendering the body mustn't pull
  // the reader back. The router sees the hash the scroll below writes (it
  // patches replaceState); that one is where the reader already is.
  const landedRef = useRef<string | null>(null)
  const scrolledHashRef = useRef<string | null>(null)
  useEffect(() => {
    const root = rootRef.current
    if (!trackSection || !root || !hash) return
    const key = `${docRef}#${hash}`
    if (landedRef.current === key) return
    if (scrolledHashRef.current === hash) {
      landedRef.current = key
      return
    }
    const heading = root.querySelector<HTMLElement>(`#${CSS.escape(hash)}`)
    if (!heading) return
    landedRef.current = key
    if (readSections.get(entryKey(docRef)) === hash) {
      // Back where the reader was; an editor landing places the view itself.
      if (landing) return
    } else {
      flashIn(root, `#${CSS.escape(hash)}`)
    }
    return holdHeading(root, heading, () => {})
  }, [trackSection, hash, htmls, docRef, landing])
  useEffect(() => {
    const root = rootRef.current
    if (!trackSection || !root) return
    const scroller = scrollParent(root)
    const target: HTMLElement | Window = scroller === document.scrollingElement ? window : scroller
    let frame = 0
    const update = () => {
      frame = 0
      const id = activeHeadingId(root, scroller)
      scrolledHashRef.current = id
      if (id) readSections.set(entryKey(docRef), id)
      else readSections.delete(entryKey(docRef))
      replaceUrlHash(id)
    }
    const onScroll = () => {
      if (!frame) frame = requestAnimationFrame(update)
    }
    target.addEventListener('scroll', onScroll, { passive: true })
    return () => {
      target.removeEventListener('scroll', onScroll)
      cancelAnimationFrame(frame)
    }
  }, [trackSection, htmls, docRef])
  const onPick = (pointer: string, name: string) => {
    navigator.clipboard.writeText(pointer).then(
      () => toast('success', `"${name}" pointer copied`),
      () => toast('error', 'Could not copy the pointer'),
    )
  }
  // A diagram node's link badge: refs only, so the parent never matters.
  const followRef = (ref: string) => {
    if (onInternalLink) {
      onInternalLink(ref)
      return
    }
    const t = targetOf(ref, true)
    if (t) void router.navigate(t as never)
  }
  const copyTarget = (target: string) => {
    navigator.clipboard.writeText(target).then(
      () => toast('success', `Link copied: ${target}`),
      () => toast('error', 'Could not copy the link'),
    )
  }
  const onClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (isSectionLinkClick(e.target)) {
      e.preventDefault()
      const link = sectionLinkAt(e.target, docRef)
      if (!link) return
      navigator.clipboard.writeText(link).then(
        () => toast('success', 'Section link copied'),
        () => toast('error', 'Could not copy the section link'),
      )
      return
    }
    // Task-list checkbox toggle — handled before the link path since a
    // checkbox is never inside an anchor. preventDefault keeps the rendered
    // box in sync with the source: it only flips once the rewritten body
    // round-trips back through `body`, not on the raw native toggle.
    if (onEditBody) {
      const box = (e.target as HTMLElement).closest(
        'input[data-task-checkbox]',
      ) as HTMLInputElement | null
      if (box) {
        e.preventDefault()
        const boxes = Array.from(
          e.currentTarget.querySelectorAll('input[data-task-checkbox]'),
        )
        const index = boxes.indexOf(box)
        if (index >= 0) void onEditBody(toggleTaskListItem(body ?? '', index))
        return
      }
    }
    if (e.defaultPrevented) return
    if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return
    const image = (e.target as HTMLElement).closest(`img[${RAW_IMAGE_ATTR}]`) as HTMLImageElement | null
    if (image && !image.closest('a')) {
      setZoomed({ src: image.src, alt: image.alt })
      return
    }
    const target = (e.target as HTMLElement).closest('a') as HTMLAnchorElement | null
    if (!target) return
    // Linkified `bucket:slug` code-spans carry the ref in data-ref,
    // so they skip the isInternalHref protocol guard. resolveLink recognises
    // the bare ref token. A rewritten relative link keeps its raw target in
    // data-href (its href is the SPA route).
    const dataRef = target.getAttribute('data-ref')
    const raw = dataRef ?? target.getAttribute('data-href') ?? target.getAttribute('href') ?? ''
    // An in-page `#anchor` (a table of contents) scrolls inside the view's own
    // scroller; the browser's default would change the route's hash instead.
    if (!dataRef && raw.length > 1 && raw.startsWith('#')) {
      let id = raw.slice(1)
      try { id = decodeURIComponent(id) } catch { /* a stray % stays literal */ }
      const heading = e.currentTarget.querySelector<HTMLElement>(`#${CSS.escape(id)}`)
      if (!heading) return
      e.preventDefault()
      heading.scrollIntoView({ block: 'start' })
      flashIn(e.currentTarget, `#${CSS.escape(id)}`)
      return
    }
    if (!dataRef && !isInternalHref(raw)) return
    if (onInternalLink) {
      e.preventDefault()
      onInternalLink(raw)
      return
    }
    // Hosts without their own link routing still follow a ref in-app.
    const t = targetOf(raw, dataRef !== null)
    if (!t) return
    e.preventDefault()
    void router.navigate(t as never)
  }
  return (
    <div
      ref={rootRef}
      className={
        `prose ${proseSizeClass(size)} dark:prose-invert max-w-none ` +
        'prose-headings:font-semibold prose-code:before:hidden prose-code:after:hidden [&>div:not([data-fence])>:first-child]:mt-0'
      }
      onClick={onClick}
      onDoubleClick={(e) => {
        // A quick second copy click mustn't open the editor.
        if (isSectionLinkClick(e.target)) e.stopPropagation()
      }}
    >
      {chunks.map((c, i) =>
        c.kind === 'md' ? (
          <div
            key={i}
            dangerouslySetInnerHTML={{ __html: htmls[i]! }}
          />
        ) : c.kind === 'svg' ? (
          <div key={i} data-fence="" data-line={c.line}>
            {linkParent === undefined ? (
              <p className={`not-prose my-3 italic text-zinc-500 ${notProseSizeClass(size)}`}>
                {c.text} isn't shown here: this view doesn't know the doc's folder
              </p>
            ) : (
              <SvgFileBlock
                path={joinPosix(linkParent, c.text)}
                file={c.text}
                alt={c.alt ?? ''}
                theme={theme}
                docRef={docRef}
                statusClassName={notProseSizeClass(size)}
                onPick={onPick}
                onNotify={toast}
              />
            )}
          </div>
        ) : (
          <div key={i} data-fence="" data-line={c.line}>
            <MermaidBlock
              code={c.text}
              theme={theme}
              themeVariables={themeVariables}
              statusClassName={notProseSizeClass(size)}
              docRef={docRef}
              fence={fences[i]}
              onPick={onPick}
              onEditCode={onEditBody && ((code) => onEditBody(replaceFence(body ?? '', fences[i]!, code)))}
              onEditError={(msg) => toast('error', `Diagram not changed, it doesn't render: ${msg}`)}
              onNotify={toast}
              canFollow={isDocRef}
              onFollow={followRef}
              onCopy={copyTarget}
            />
          </div>
        ),
      )}
      {zoomed && <ImageOverlay src={zoomed.src} alt={zoomed.alt} onClose={closeZoomed} />}
    </div>
  )
}
