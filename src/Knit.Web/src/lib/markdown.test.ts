import { describe, it, expect } from 'vitest'
import { renderMarkdown, bodyIsRenderEmpty, withRawFiles, withSpaHrefs } from './markdown'
import { HeadingSlugger, extractOutline } from './outline'

// Pull the id="" off every h2/h3 in render order — the anchors the scroll-sync
// + outline highlight depend on.
function renderedHeadingIds(body: string): string[] {
  const slugger = new HeadingSlugger()
  const html = renderMarkdown(body, slugger)
  return [...html.matchAll(/<h[23]\s+id="([^"]*)"/g)].map((m) => m[1]!)
}

describe('bodyIsRenderEmpty', () => {
  it('is true for a blank / whitespace-only body', () => {
    expect(bodyIsRenderEmpty('')).toBe(true)
    expect(bodyIsRenderEmpty('   \n\n')).toBe(true)
  })

  it('is true for an H1-only body when no title is given (H1 stripped unconditionally)', () => {
    expect(bodyIsRenderEmpty('# foo\n')).toBe(true)
  })

  it('is true for an H1-only body when the H1 mirrors the title', () => {
    expect(bodyIsRenderEmpty('# Prototype pilot\n', 'Prototype pilot')).toBe(true)
  })

  it('is false when the H1 drifts from the title (drift stays visible)', () => {
    expect(bodyIsRenderEmpty('# Old name\n', 'New name')).toBe(false)
  })

  it('is false once there is prose beyond the H1', () => {
    expect(bodyIsRenderEmpty('# foo\n\nsome text\n')).toBe(false)
    expect(bodyIsRenderEmpty('# foo\n\nsome text\n', 'foo')).toBe(false)
  })
})

describe('renderMarkdown — kbRef code-span linkifier', () => {
  it('linkifies a bare bucket:slug code-span into a data-ref anchor', () => {
    const html = renderMarkdown('See `adr:my-decision` for context.')
    expect(html).toContain('<a data-ref="adr:my-decision" class="kb-ref">adr:my-decision</a>')
  })

  it('linkifies folder-form sub-doc refs', () => {
    const html = renderMarkdown('`feature:assembly-line/wiring`')
    expect(html).toContain('data-ref="feature:assembly-line/wiring"')
  })

  it('leaves a code-span carrying extra prose as a plain code element', () => {
    const html = renderMarkdown('`adr:my-decision and more`')
    expect(html).not.toContain('data-ref')
    expect(html).toContain('<code>')
  })

  it('linkifies an item ref with the full id', () => {
    const html = renderMarkdown('See `item:G7H8K2M4P9Q1R5T3VX` "Add column".')
    expect(html).toContain('<a data-ref="item:G7H8K2M4P9Q1R5T3VX" class="kb-ref">item:G7H8K2M4P9Q1R5T3VX</a>')
  })

  it('leaves a short item prefix as a plain code-span', () => {
    const html = renderMarkdown('`item:G7H8`')
    expect(html).not.toContain('data-ref')
    expect(html).toContain('<code>')
  })

  it('linkifies a project ref', () => {
    const html = renderMarkdown('`project:launch-2`')
    expect(html).toContain('data-ref="project:launch-2"')
  })

  it('keeps the data-ref attribute through DOMPurify sanitization', () => {
    const html = renderMarkdown('`doc:overview`')
    expect(html).toContain('data-ref="doc:overview"')
  })
})

describe('renderMarkdown — task-list checkboxes', () => {
  it('emits enabled, tagged checkboxes (not the default disabled style)', () => {
    const html = renderMarkdown('- [ ] todo\n- [x] done')
    expect(html).not.toContain('disabled')
    expect(html).toContain('data-task-checkbox')
    expect(html).toContain('type="checkbox"')
  })

  it('marks a checked item with the checked attribute', () => {
    const html = renderMarkdown('- [x] done')
    expect(html).toContain('checked=""')
  })

  it('renders bare (empty) task items as checkboxes, not literal "[ ]"', () => {
    const html = renderMarkdown('- [ ]\n- [x]')
    // two enabled checkboxes, one checked, and no leftover bracket text
    expect(html.match(/data-task-checkbox/g)).toHaveLength(2)
    expect(html).toContain('checked=""')
    expect(html).not.toContain('[ ]')
    expect(html).not.toContain('[x]')
  })

  it('leaves a non-checkbox bracket item as plain text', () => {
    // `[] a` is not a checkbox (empty brackets) — must stay a normal bullet
    const html = renderMarkdown('- [] a')
    expect(html).not.toContain('data-task-checkbox')
    expect(html).toContain('[] a')
  })

  it('does not treat `[ ]a` (no space after the box) as a task', () => {
    const html = renderMarkdown('- [ ]a')
    expect(html).not.toContain('data-task-checkbox')
  })
})

describe('renderMarkdown — frontmatter / h1 stripping', () => {
  it('strips a leading # h1 (duplicates frontmatter title)', () => {
    const html = renderMarkdown('# Title\n\nBody text.')
    expect(html).not.toContain('<h1')
    expect(html).toContain('Body text.')
  })

  it('strips a leading YAML frontmatter block', () => {
    const html = renderMarkdown('---\ntitle: X\n---\n\nBody.')
    expect(html).not.toContain('title: X')
    expect(html).toContain('Body.')
  })

  it('strips the leading h1 when it mirrors the title', () => {
    const html = renderMarkdown('# Storage layer\n\nBody.', undefined, 'Storage layer')
    expect(html).not.toContain('<h1')
    expect(html).toContain('Body.')
  })

  it('keeps a leading h1 that drifts from the title (fallback)', () => {
    const html = renderMarkdown('# Old name\n\nBody.', undefined, 'New name')
    expect(html).toContain('<h1')
    expect(html).toContain('Old name')
  })
})

describe('renderMarkdown — heading ids', () => {
  it('keeps an anchor that names a document property', () => {
    const html = renderMarkdown('## Timeline\n\n## Title\n\n## Images')
    expect(html).toContain('<h2 id="timeline">')
    expect(html).toContain('<h2 id="title">')
    expect(html).toContain('<h2 id="images">')
  })

  it('still sanitizes a raw id outside a heading', () => {
    expect(renderMarkdown('<img id="cookie" src="x.png">')).not.toContain('id="cookie"')
  })
})

describe('renderMarkdown ↔ extractOutline anchor alignment (10.2a/c)', () => {
  // The scroll-sync clicks `#<outline-id>` and highlights when the observed
  // heading id equals an outline id. If the two derivations drift, every
  // heading past the first divergence becomes unselectable.
  it('emits the same id sequence the outline produces', () => {
    const body = [
      '---',
      'title: Doc',
      '---',
      '',
      '# Doc title',
      '',
      '## Storage model',
      'text',
      '## Goals',
      '### A `code` heading',
      '## Goals',
      '#### ignored h4',
      '```',
      '## fenced not a heading',
      '```',
      '## Links like [the design](x.md) inline',
    ].join('\n')

    const outlineIds = extractOutline(body).map((o) => o.id)
    expect(renderedHeadingIds(body)).toEqual(outlineIds)
  })
})

describe('withSpaHrefs', () => {
  const spaHref = (raw: string, isRef: boolean) =>
    raw.startsWith('http') ? null : `/spa?${isRef ? 'ref' : 'path'}=${raw}`

  it('gives a ref its route and leaves its data-ref', () => {
    const html = withSpaHrefs(renderMarkdown('see `adr:my-decision`'), spaHref)
    expect(html).toContain('<a data-ref="adr:my-decision" class="kb-ref" href="/spa?ref=adr:my-decision">')
  })

  it('moves a relative target to data-href and routes the href', () => {
    const html = withSpaHrefs(renderMarkdown('[x](../docs/a.md)'), spaHref)
    expect(html).toContain('href="/spa?path=../docs/a.md"')
    expect(html).toContain('data-href="../docs/a.md"')
  })

  it('leaves a link the resolver declines untouched', () => {
    const html = withSpaHrefs(renderMarkdown('[x](https://example.com)'), spaHref)
    expect(html).toContain('<a href="https://example.com">')
    expect(html).not.toContain('data-href')
  })
})

describe('renderMarkdown — source lines', () => {
  const linesOf = (html: string, selector: string) => {
    const tpl = document.createElement('template')
    tpl.innerHTML = html
    return Array.from(tpl.content.querySelectorAll(selector)).map((el) => Number(el.getAttribute('data-line')))
  }
  const BODY = [
    '# Title', //            1 (stripped)
    '',
    'Intro para', //         3
    'second line',
    '',
    '## Heading', //         6
    '',
    '- a', //                8
    '- b', //                9
    '  wrapped',
    '- [ ] task', //        11
    '',
    '| h | i |', //         13
    '|---|---|',
    '| 1 | 2 |', //         15
    '| 3 | 4 |', //         16
    '',
    '```js', //             18
    'code',
    '```',
    '',
    '> quote', //           22
  ].join('\n')

  it('tags blocks, list items and table rows with their body line', () => {
    const html = renderMarkdown(BODY, undefined, 'Title', { firstLine: 1 })
    expect(linesOf(html, 'p[data-line]')).toEqual([3])
    expect(linesOf(html, 'h2')).toEqual([6])
    expect(linesOf(html, 'li')).toEqual([8, 9, 11])
    expect(linesOf(html, 'tr')).toEqual([13, 15, 16])
    expect(linesOf(html, 'pre')).toEqual([18])
    expect(linesOf(html, 'blockquote')).toEqual([22])
  })

  it('counts stripped frontmatter, CRLF and a chunk offset', () => {
    const body = '---\r\ntitle: X\r\n---\r\n\r\n# X\r\n\r\nBody.\r\n'
    expect(linesOf(renderMarkdown(body, undefined, 'X', { firstLine: 10 }), 'p')).toEqual([16])
  })

  it('renders the same HTML apart from the line attributes', () => {
    const body = 'See `adr:my-decision` and [ref].\n\n[ref]: https://example.com\n\n- [ ] todo\n- [x] done\n\n<div>raw</div>\n'
    const plain = renderMarkdown(body)
    const lined = renderMarkdown(body, undefined, undefined, { firstLine: 1 })
    expect(lined.replace(/ data-line="\d+"/g, '')).toBe(plain)
  })

  it('strips the title only at the body start', () => {
    const html = renderMarkdown('\n# Title\n\nAfter a diagram.', undefined, 'Title', { firstLine: 5, leading: false })
    expect(html).toContain('<h1 data-line="6">Title</h1>')
    expect(linesOf(html, 'p')).toEqual([8])
  })
})

describe('withRawFiles', () => {
  const raw = (t: string) => `/api/raw?path=${encodeURIComponent(`.knit/kb/docs/${t}`)}`

  it('serves a relative raster image from the raw endpoint and marks it zoomable', () => {
    const html = withRawFiles(renderMarkdown('![Shot](shot.png)'), raw)
    expect(html).toContain('src="/api/raw?path=.knit%2Fkb%2Fdocs%2Fshot.png"')
    expect(html).toContain('data-raw-image=""')
  })

  it('opens a PDF link from the raw endpoint in a new tab, past the SPA routing', () => {
    const html = withSpaHrefs(withRawFiles(renderMarkdown('[spec](spec.pdf)'), raw), () => '/file?path=x')
    expect(html).toContain('href="/api/raw?path=.knit%2Fkb%2Fdocs%2Fspec.pdf"')
    expect(html).toContain('target="_blank"')
    expect(html).not.toContain('data-href')
  })

  it('leaves other links and images alone', () => {
    const html = withRawFiles(renderMarkdown('![a](a.svg) [b](b.md) ![c](c.png)'), () => null)
    expect(html).not.toContain('data-raw')
  })
})
