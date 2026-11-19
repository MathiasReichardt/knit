import { withoutFrontmatter } from './frontmatter'

/**
 * A diagram's direction lives in its text, not in config: a header token
 * (`flowchart LR`, `timeline TD`) or a top-level `direction LR` statement
 * (state, class). Read and rewrite just that; everything else stays as written.
 */
export type DirectionSyntax = 'flowchart' | 'timeline' | 'statement'

export const DIRECTIONS: Record<DirectionSyntax, string[]> = {
  // Menu order, two per row: each row a pair of opposites.
  flowchart: ['LR', 'TB', 'RL', 'BT'],
  statement: ['LR', 'TB', 'RL', 'BT'],
  timeline: ['LR', 'TD'],
}

export const DEFAULT_DIRECTION: Record<DirectionSyntax, string> = {
  flowchart: 'TB',
  statement: 'TB',
  timeline: 'LR',
}

const HEADER: Record<'flowchart' | 'timeline', RegExp> = {
  flowchart: /^([ \t]*)(flowchart|graph)(?=[\s;]|$)(?:([ \t]+)([^\s;]+))?(.*)$/,
  timeline: /^([ \t]*)(timeline)(?=\s|$)(?:([ \t]+)(\S+))?(.*)$/,
}
const STATEMENT = /^([ \t]*)direction[ \t]+(\S+)[ \t]*$/

interface Line {
  start: number
  end: number
  text: string
}

function* linesFrom(code: string, pos: number): Generator<Line> {
  while (pos <= code.length) {
    const nl = code.indexOf('\n', pos)
    const end = nl < 0 ? code.length : nl
    yield { start: pos, end, text: code.slice(pos, end) }
    if (nl < 0) return
    pos = nl + 1
  }
}

const trivia = (text: string) => /^\s*(%%.*)?$/.test(text)

/** The diagram's first line: after the frontmatter, directives, comments and blank lines. */
function headerLine(code: string): Line | null {
  for (const l of linesFrom(code, code.length - withoutFrontmatter(code).length)) if (!trivia(l.text)) return l
  return null
}

const splice = (code: string, start: number, end: number, text: string) => code.slice(0, start) + text + code.slice(end)

interface Found {
  /** The written token, as is. */
  token: string | undefined
  /** The fence with the direction set, or the token removed when undefined. */
  write: (dir: string | undefined) => string
}

function locate(code: string, syntax: DirectionSyntax): Found | null {
  const h = headerLine(code)
  if (!h) return null
  if (syntax !== 'statement') {
    const m = HEADER[syntax].exec(h.text)
    if (!m) return null
    const [, indent, keyword, gap, token, rest] = m
    return {
      token,
      write: (dir) => splice(code, h.start, h.end, `${indent}${keyword}${dir ? `${gap ?? ' '}${dir}` : ''}${rest}`),
    }
  }
  // Only a top-level statement: one inside a composite state or namespace sets that block's direction.
  let depth = 0
  let firstIndent: string | undefined
  for (const l of linesFrom(code, h.end + 1)) {
    if (trivia(l.text)) continue
    firstIndent ??= /^[ \t]*/.exec(l.text)![0]
    const m = depth === 0 ? STATEMENT.exec(l.text) : null
    if (m) {
      const [, indent, token] = m
      return {
        token,
        write: (dir) =>
          dir ? splice(code, l.start, l.end, `${indent}direction ${dir}`) : splice(code, l.start - 1, l.end, ''),
      }
    }
    depth += (l.text.match(/\{/g)?.length ?? 0) - (l.text.match(/\}/g)?.length ?? 0)
  }
  return {
    token: undefined,
    write: (dir) => (dir ? splice(code, h.end, h.end, `\n${firstIndent || '  '}direction ${dir}`) : code),
  }
}

const normalize = (syntax: DirectionSyntax, token: string | undefined) =>
  token === 'TD' && syntax !== 'timeline' ? 'TB' : token

/**
 * The written direction: undefined for the default (or none written), null
 * when the diagram text has a form this doesn't know.
 */
export function readDirection(code: string, syntax: DirectionSyntax): string | undefined | null {
  const f = locate(code, syntax)
  if (!f) return null
  const dir = normalize(syntax, f.token)
  if (dir === undefined || dir === DEFAULT_DIRECTION[syntax]) return undefined
  return DIRECTIONS[syntax].includes(dir) ? dir : null
}

/**
 * The fence with its direction set. The default (or undefined) drops the
 * token, except in a flowchart header, which keeps an explicit `TB`. Null when
 * not editable.
 */
export function writeDirection(code: string, syntax: DirectionSyntax, dir: string | undefined): string | null {
  const f = locate(code, syntax)
  if (!f) return null
  if (f.token !== undefined && !DIRECTIONS[syntax].includes(normalize(syntax, f.token)!)) return null
  const target = dir === DEFAULT_DIRECTION[syntax] ? undefined : dir
  if (target === undefined) {
    if (f.token === undefined) return code
    return f.write(syntax === 'flowchart' ? 'TB' : undefined)
  }
  return f.write(target)
}
