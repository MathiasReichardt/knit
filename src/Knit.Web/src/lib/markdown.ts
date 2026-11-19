import { marked, type Token, type Tokens, type TokensList } from 'marked'
import DOMPurify from 'dompurify'
import { HeadingSlugger } from './outline'

marked.setOptions({ gfm: true, breaks: false })

// Set for the duration of one synchronous `marked.parse` so the heading
// renderer can assign deduped anchor ids in document order. A body split
// across mermaid chunks shares one slugger (see MarkdownRender) so ids
// stay aligned with `extractOutline`. JS is single-threaded and parse is
// sync, so a module-level handle is safe.
let activeSlugger: HeadingSlugger | null = null
let activeSectionLinks = false

// lucide `link`, inlined: the renderer emits an HTML string, not React.
const LINK_ICON =
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
  'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
  '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/>' +
  '<path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/></svg>'

/** Marks the copy-section-link button; MarkdownRender's click handler copies `<docRef>#<heading id>`. */
export const SECTION_LINK_ATTR = 'data-section-link'
const SECTION_LINK_BUTTON =
  `<button type="button" class="section-link" ${SECTION_LINK_ATTR}="" ` +
  `title="Copy link to section" aria-label="Copy link to section">${LINK_ICON}</button>`

// A backtick code-span whose entire content is a canonical ref: a KB
// `bucket:slug`, `design:`, `project:`, `item:<full-id>`, or a `.knit/…/x.md`
// path for a doc without a ref — each with an optional `#<anchor>`. Mirrors
// KB_REF_RE / DESIGN_REF_RE / PROJECT_REF_RE / ITEM_REF_RE in resolveLink.ts
// (the front-end form of the C# Ref grammar) — kept as a separate literal
// because this one is anchored to the surrounding backticks. Only
// single-backtick spans are linkified; a span carrying extra prose, or an
// `item:` prefix, is left as a plain code-span. The link is syntactic: a
// dead ref still renders as one, and the click probe reports it.
const KB_REF_CODESPAN_RE =
  /^`((?:(?:adr|doc|feature|log|idea|source|design):[a-z0-9][a-z0-9/-]*|project:[a-z0-9][a-z0-9-]*|item:[0-9A-Za-z]{18}|\.knit\/[^`\s#\\]+\.md)(?:#[^`\s]+)?)`/

interface KbRefToken extends Tokens.Generic {
  type: 'kbRef'
  ref: string
}

const escapeAttr = (s: string) =>
  s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;')

// An empty task-list item — list marker, the checkbox, nothing after.
// marked only flags an item as a task when content follows the box
// (/^\[[ xX]\] +\S/), so a bare `- [ ]` / `- [x]` falls through as the
// literal text `[ ]`. GitHub renders those as checkboxes; so do we.
const EMPTY_TASK_ITEM_RE = /^\s*(?:[-*+]|\d+[.)])\s+\[([ xX])\]\s*$/

// Render-only: GFM task-list checkboxes default to `disabled`. Emit them
// enabled and tagged with `data-task-checkbox` so MarkdownRender's click
// handler can map a clicked box to the Nth task token in the source body
// The editor body grammar is untouched — toggling rewrites the raw
// markdown and round-trips through the normal save path.
// DOMPurify drops an id that names a `document` property (anti-clobbering),
// which would leave `## Timeline` or `## Title` without its anchor. A heading
// can't clobber `document`, and a slug can't carry markup.
let headingIdHook = false
function keepHeadingIds(): void {
  if (headingIdHook) return
  headingIdHook = true
  DOMPurify.addHook('uponSanitizeAttribute', (node, data) => {
    if (data.attrName === 'id' && /^H[23]$/.test(node.nodeName) && /^[a-z0-9-]+$/.test(data.attrValue)) {
      data.forceKeepAttr = true
    }
  })
}

function taskCheckbox(checked: boolean): string {
  return `<input ${checked ? 'checked="" ' : ''}type="checkbox" data-task-checkbox=""> `
}

// Render-only: turn a `bucket:slug` code-span into an anchor the
// MarkdownRender click handler can resolve. The ref rides a `data-ref`
// attribute (not `href`) so it survives DOMPurify untouched and doesn't
// trip the `^[a-z]+:` protocol guard in isInternalHref. Non-matching
// code-spans return undefined so marked falls through to its default
// `codespan` tokenizer. The editor is deliberately NOT extended.
marked.use({
  renderer: {
    checkbox({ checked }: Tokens.Checkbox): string {
      return taskCheckbox(checked)
    },
    // Promote a bare `- [ ]` / `- [x]` (which marked misses) to a real
    // checkbox item; defer to the default renderer for everything else by
    // returning false. Keeps the rendered checkbox count aligned with
    // toggleTaskListItem's token count so click→token mapping holds.
    listitem(item: Tokens.ListItem): string | false {
      if (!item.task) {
        const m = EMPTY_TASK_ITEM_RE.exec(item.raw ?? '')
        if (m) return `<li>${taskCheckbox(m[1] !== ' ')}</li>\n`
      }
      return false
    },
    // Emit stable `id=` anchors on h2/h3 so the Designs/KB outline can
    // jump to a section. The id is sourced from the same slugger
    // `extractOutline` uses, keeping anchors and the outline in lockstep.
    heading(this: { parser: { parseInline(tokens: Tokens.Generic[]): string } }, token: Tokens.Heading): string {
      const inner = this.parser.parseInline(token.tokens)
      if (token.depth === 2 || token.depth === 3) {
        const id = (activeSlugger ?? new HeadingSlugger()).slug(token.text)
        const link = activeSectionLinks ? SECTION_LINK_BUTTON : ''
        return `<h${token.depth} id="${id}">${inner}${link}</h${token.depth}>\n`
      }
      return `<h${token.depth}>${inner}</h${token.depth}>\n`
    },
  },
  extensions: [
    {
      name: 'kbRef',
      level: 'inline',
      start(src: string) {
        return src.indexOf('`')
      },
      tokenizer(src: string): KbRefToken | undefined {
        const m = KB_REF_CODESPAN_RE.exec(src)
        if (!m) return undefined
        return { type: 'kbRef', raw: m[0], ref: m[1]! }
      },
      renderer(token: Tokens.Generic) {
        const ref = escapeAttr((token as KbRefToken).ref)
        return `<a data-ref="${ref}" class="kb-ref">${ref}</a>`
      },
    },
  ],
})

/**
 * Render an item / KB / design body to sanitized HTML. The leading
 * `# heading` line is stripped when it mirrors the frontmatter `title`
 * (it would just duplicate the header). A leading H1 that *differs* from `title` is a drift signal
 * and is surfaced rather than hidden. When no `title` is supplied
 * (e.g. a folder index body with no title context) the strip stays
 * unconditional. A leading YAML frontmatter block would already be
 * removed by the server, but we strip defensively in case a raw body
 * is passed in.
 */
export function renderMarkdown(
  body: string,
  slugger?: HeadingSlugger,
  title?: string,
  { firstLine, leading = true, sectionLinks = false }: RenderOptions = {},
): string {
  const input = (body ?? '').replace(/\r\n/g, '\n')
  const cleaned = leading ? stripFrontmatterAndH1(input, title) : input
  activeSlugger = slugger ?? new HeadingSlugger()
  activeSectionLinks = sectionLinks
  let html: string
  try {
    html =
      firstLine === undefined
        ? (marked.parse(cleaned, { async: false }) as string)
        : parseWithLines(cleaned, firstLine + lineCount(input) - lineCount(cleaned))
  } finally {
    activeSlugger = null
    activeSectionLinks = false
  }
  // data-* survives DOMPurify by default, but pin the attributes we depend
  // on explicitly so the kbRef anchors, task checkboxes, heading anchors and
  // source lines can't be stripped by a future config change.
  keepHeadingIds()
  const clean = DOMPurify.sanitize(html, {
    ADD_ATTR: ['data-ref', 'data-task-checkbox', 'id', 'data-line', 'data-item-lines', SECTION_LINK_ATTR],
  })
  return firstLine === undefined ? clean : spreadLines(clean)
}

export interface RenderOptions {
  /**
   * The 1-based body line `body` starts on. When set, each top-level block,
   * list item and table row carries `data-line` (its first source line), so
   * a spot in the view maps back to the editor.
   */
  firstLine?: number
  /** False for a chunk after a mermaid fence: the frontmatter / `# title` strip applies only at the body's start. */
  leading?: boolean
  /** Give each h2/h3 a hover button that copies a link to its section. */
  sectionLinks?: boolean
}

const lineCount = (s: string) => s.split('\n').length
const newlines = (s: string) => lineCount(s) - 1

// The top-level tokens' `raw` concatenate to the source, so a block's line is
// the newline count before it. Each token renders on its own (sharing the
// link definitions) so its first tag can take the line.
function parseWithLines(src: string, firstLine: number): string {
  const tokens = marked.lexer(src)
  let line = firstLine
  let out = ''
  for (const token of tokens) {
    const one = Object.assign([token], { links: tokens.links }) as TokensList
    out += tagLine(marked.parser(one), token, line)
    line += newlines(token.raw)
  }
  return out
}

function tagLine(html: string, token: Token, line: number): string {
  let attrs = `data-line="${line}"`
  if (token.type === 'list') {
    let at = line
    const lines = (token as Tokens.List).items.map((item) => {
      const l = at
      at += newlines(item.raw)
      return l
    })
    attrs += ` data-item-lines="${lines.join(',')}"`
  }
  return html.replace(/^<([a-zA-Z][\w-]*)/, `<$1 ${attrs}`)
}

// Hands a list's item lines to its own `<li>`s, and each table row its line
// (the header, the delimiter row, then one line per body row).
function spreadLines(html: string): string {
  const tpl = document.createElement('template')
  tpl.innerHTML = html
  for (const list of tpl.content.querySelectorAll('[data-item-lines]')) {
    const lines = list.getAttribute('data-item-lines')!.split(',')
    list.removeAttribute('data-item-lines')
    Array.from(list.children)
      .filter((li) => li.tagName === 'LI')
      .forEach((li, i) => {
        if (lines[i]) li.setAttribute('data-line', lines[i])
      })
  }
  for (const table of tpl.content.querySelectorAll<HTMLTableElement>('table[data-line]')) {
    const first = Number(table.getAttribute('data-line'))
    for (const row of Array.from(table.tHead?.rows ?? [])) row.setAttribute('data-line', String(first))
    Array.from(table.tBodies[0]?.rows ?? []).forEach((row, i) =>
      row.setAttribute('data-line', String(first + 2 + i)),
    )
  }
  return tpl.innerHTML
}

/**
 * Gives in-repo links a real SPA href, so middle-click, Ctrl+click and the
 * browser context menu open them in a new tab. A plain link keeps its raw
 * target in `data-href` for the in-app click handler; a ref already has it in
 * `data-ref`. `spaHref` returns null to leave an anchor untouched.
 */
export function withSpaHrefs(
  html: string,
  spaHref: (raw: string, isRef: boolean) => string | null,
): string {
  const tpl = document.createElement('template')
  tpl.innerHTML = html
  for (const a of tpl.content.querySelectorAll('a')) {
    if (a.hasAttribute(RAW_FILE_ATTR)) continue
    const ref = a.getAttribute('data-ref')
    const raw = ref ?? a.getAttribute('href')
    if (!raw) continue
    const href = spaHref(raw, ref !== null)
    if (!href) continue
    if (ref === null) a.setAttribute('data-href', raw)
    a.setAttribute('href', href)
  }
  return tpl.innerHTML
}

/** Marks a PDF link {@link withRawFiles} pointed at the raw-file endpoint; the browser opens it. */
export const RAW_FILE_ATTR = 'data-raw-file'
/** Marks an image served from the raw-file endpoint; a click shows it maximized. */
export const RAW_IMAGE_ATTR = 'data-raw-image'

const RAW_IMAGE_RE = /\.(png|jpe?g|gif|webp|avif)$/i
const RAW_LINK_RE = /\.pdf$/i

/**
 * Serves a relative image (`![x](shot.png)`) and a PDF link from the raw-file
 * endpoint: an `<img src>` would otherwise resolve against the SPA route, and
 * the code viewer can't show a PDF. A PDF opens in a new tab, in the browser's
 * viewer. `rawUrl` returns null for a target it doesn't serve.
 */
export function withRawFiles(html: string, rawUrl: (target: string) => string | null): string {
  const tpl = document.createElement('template')
  tpl.innerHTML = html
  for (const img of tpl.content.querySelectorAll('img')) {
    const src = img.getAttribute('src') ?? ''
    const url = RAW_IMAGE_RE.test(src.split(/[?#]/)[0]!) ? rawUrl(src) : null
    if (!url) continue
    img.setAttribute('src', url)
    img.setAttribute(RAW_IMAGE_ATTR, '')
  }
  for (const a of tpl.content.querySelectorAll('a[href]')) {
    const href = a.getAttribute('href')!
    const url = RAW_LINK_RE.test(href.split(/[?#]/)[0]!) ? rawUrl(href) : null
    if (!url) continue
    a.setAttribute('href', url)
    a.setAttribute('target', '_blank')
    a.setAttribute('rel', 'noopener')
    a.setAttribute(RAW_FILE_ATTR, '')
  }
  return tpl.innerHTML
}

/**
 * True when a body has no content that {@link renderMarkdown} would show —
 * i.e. it's blank, or only the leading `# H1` that gets stripped (either
 * unconditionally when no `title` is supplied, or when it mirrors `title`).
 * Hosts use this to decide between rendering and the "start writing" hint:
 * gating on raw `body.trim()` alone leaves an H1-only body (the scaffolded
 * default for milestones / project docs) rendering to nothing with no
 * editable target.
 */
export function bodyIsRenderEmpty(body: string, title?: string): boolean {
  return stripFrontmatterAndH1(body ?? '', title).trim() === ''
}

function stripFrontmatterAndH1(input: string, title?: string): string {
  let s = input.replace(/\r\n/g, '\n')
  if (s.startsWith('---\n')) {
    const end = s.indexOf('\n---\n', 4)
    if (end >= 0) s = s.slice(end + 5)
  }
  s = s.replace(/^\s*\n+/, '')
  if (s.startsWith('# ')) {
    const nl = s.indexOf('\n')
    const h1 = (nl >= 0 ? s.slice(2, nl) : s.slice(2)).trim()
    // Render fallback: only strip the leading H1 when it mirrors the
    // title (or when no title context is supplied). A drifted H1 stays
    // visible so the mismatch surfaces.
    if (title === undefined || h1 === title.trim()) {
      s = nl >= 0 ? s.slice(nl + 1) : ''
      s = s.replace(/^\s*\n+/, '')
    }
  }
  return s
}
