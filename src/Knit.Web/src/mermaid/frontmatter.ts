/**
 * Read and write `config:` keys in a mermaid fence's YAML frontmatter, line by
 * line, so every other line (title, other keys, comments) stays as written.
 * Handles block-style maps with scalar leaves, the form the layout menu writes;
 * anything else on the path (flow maps, lists, block scalars, tabs) is reported
 * as not editable rather than rewritten.
 */

export type Scalar = string | number | boolean

const OPEN = /^---[ \t]*\n/
const CLOSE = /^---[ \t]*$/m
const ENTRY = /^( *)([A-Za-z_][\w.-]*):(?:[ \t]+(.*?))?[ \t]*$/

interface Entry {
  idx: number
  indent: number
  key: string
  rest: string
}

interface Split {
  lines: string[]
  /** The diagram text after the closing `---` line. */
  rest: string
}

function split(code: string): Split | null | undefined {
  const open = OPEN.exec(code)
  if (!open) return undefined
  const after = code.slice(open[0].length)
  const close = CLOSE.exec(after)
  if (!close) return null
  const inner = after.slice(0, close.index)
  const tail = after.slice(close.index + close[0].length)
  return {
    lines: inner === '' ? [] : inner.replace(/\n$/, '').split('\n'),
    rest: tail.startsWith('\n') ? tail.slice(1) : tail,
  }
}

const trivia = (line: string) => /^\s*(#.*)?$/.test(line)
const indentOf = (line: string) => line.length - line.trimStart().length
const isComment = (rest: string) => rest === '' || rest.startsWith('#')

/** Child entries of the map spanning lines [start, end); null when a child line isn't a simple `key:` entry. */
function entries(lines: string[], start: number, end: number): Entry[] | null {
  const out: Entry[] = []
  let childIndent = -1
  for (let i = start; i < end; i++) {
    const line = lines[i]!
    if (trivia(line)) continue
    if (/^\s*\t/.test(line)) return null
    const ind = indentOf(line)
    if (childIndent < 0) childIndent = ind
    if (ind > childIndent) continue
    if (ind < childIndent) return null
    const m = ENTRY.exec(line)
    if (!m) return null
    out.push({ idx: i, indent: ind, key: m[2]!, rest: m[3] ?? '' })
  }
  return out
}

/** End (exclusive) of the lines nested under the entry at `idx`. */
function blockEnd(lines: string[], idx: number, end: number): number {
  const ind = indentOf(lines[idx]!)
  let last = idx + 1
  for (let i = idx + 1; i < end; i++) {
    const line = lines[i]!
    if (trivia(line)) continue
    if (indentOf(line) <= ind) break
    last = i + 1
  }
  return last
}

function lastContent(lines: string[], start: number, end: number): number {
  for (let i = end - 1; i >= start; i--) if (!trivia(lines[i]!)) return i + 1
  return start
}

function parseScalar(raw: string): Scalar | null {
  if (raw === '' || /^[[{|>&*!#]/.test(raw)) return null
  const quoted = /^"((?:[^"\\]|\\.)*)"(?:\s+#.*)?$/.exec(raw) ?? /^'((?:[^']|'')*)'(?:\s+#.*)?$/.exec(raw)
  if (quoted) return raw.startsWith('"') ? (JSON.parse(`"${quoted[1]}"`) as string) : quoted[1]!.replace(/''/g, "'")
  const v = raw.replace(/\s+#.*$/, '')
  if (v === 'true') return true
  if (v === 'false') return false
  if (/^-?\d+(\.\d+)?$/.test(v)) return Number(v)
  return v
}

function formatScalar(v: Scalar): string {
  if (typeof v !== 'string') return String(v)
  return /^[A-Za-z][\w-]*$/.test(v) && v !== 'true' && v !== 'false' && v !== 'null' ? v : JSON.stringify(v)
}

const NOT_EDITABLE = Symbol('not editable')

function getIn(lines: string[], start: number, end: number, path: string[]): Scalar | undefined | typeof NOT_EDITABLE {
  const kids = entries(lines, start, end)
  if (!kids) return NOT_EDITABLE
  const k = kids.find((c) => c.key === path[0])
  if (!k) return undefined
  const kEnd = blockEnd(lines, k.idx, end)
  if (path.length === 1) {
    const v = parseScalar(k.rest)
    return v === null || kEnd > k.idx + 1 ? NOT_EDITABLE : v
  }
  if (!isComment(k.rest)) return NOT_EDITABLE
  return getIn(lines, k.idx + 1, kEnd, path.slice(1))
}

function nested(indent: number, path: string[], value: Scalar): string[] {
  const pad = ' '.repeat(indent)
  if (path.length === 1) return [`${pad}${path[0]}: ${formatScalar(value)}`]
  return [`${pad}${path[0]}:`, ...nested(indent + 2, path.slice(1), value)]
}

/** Sets or removes `path` within lines [start, end); returns the new end, or null when not editable. */
function setIn(
  lines: string[],
  start: number,
  end: number,
  childIndent: number,
  path: string[],
  value: Scalar | undefined,
): number | null {
  const kids = entries(lines, start, end)
  if (!kids) return null
  const indent = kids[0]?.indent ?? childIndent
  const k = kids.find((c) => c.key === path[0])
  if (!k) {
    if (value === undefined) return end
    const add = nested(indent, path, value)
    lines.splice(lastContent(lines, start, end), 0, ...add)
    return end + add.length
  }
  const kEnd = blockEnd(lines, k.idx, end)
  if (path.length === 1) {
    if (parseScalar(k.rest) === null || kEnd > k.idx + 1) return null
    if (value === undefined) {
      lines.splice(k.idx, 1)
      return end - 1
    }
    lines[k.idx] = nested(indent, path, value)[0]!
    return end
  }
  if (!isComment(k.rest)) return null
  const newKEnd = setIn(lines, k.idx + 1, kEnd, indent + 2, path.slice(1), value)
  if (newKEnd === null) return null
  const delta = newKEnd - kEnd
  if (lastContent(lines, k.idx + 1, newKEnd) === k.idx + 1) {
    // Drop the emptied map with whatever trivia it held.
    lines.splice(k.idx, newKEnd - k.idx)
    return end + delta - (newKEnd - k.idx)
  }
  return end + delta
}

/**
 * The value at `config.<path>` in the fence's frontmatter: undefined when unset,
 * null when the frontmatter on that path isn't a form this module edits.
 */
export function readConfig(code: string, path: string[]): Scalar | undefined | null {
  const s = split(code)
  if (s === undefined) return undefined
  if (s === null) return null
  const v = getIn(s.lines, 0, s.lines.length, ['config', ...path])
  return v === NOT_EDITABLE ? null : v
}

/**
 * The fence with `config.<path>` set to `value`, or removed when `value` is
 * undefined (emptied maps and an emptied frontmatter go with it). Null when the
 * frontmatter on that path isn't a form this module edits.
 */
export function writeConfig(code: string, path: string[], value: Scalar | undefined): string | null {
  const s = split(code)
  if (s === null) return null
  if (s === undefined) {
    if (value === undefined) return code
    return ['---', ...nested(0, ['config', ...path], value), '---', code].join('\n')
  }
  const lines = s.lines.slice()
  if (setIn(lines, 0, lines.length, 0, ['config', ...path], value) === null) return null
  if (lines.every(trivia)) return s.rest
  return ['---', ...lines, '---', s.rest].join('\n')
}

/** The fence's diagram text after its frontmatter block. */
export function withoutFrontmatter(code: string): string {
  return split(code)?.rest ?? code
}
