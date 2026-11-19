export interface Chunk {
  kind: 'md' | 'mermaid' | 'svg'
  /** A mermaid chunk: its code; an svg chunk: the image link's target. */
  text: string
  /** 1-based body line the chunk starts on (a mermaid chunk: its opening fence). */
  line: number
  /** An svg chunk: the image's alt text. */
  alt?: string
}

const MERMAID_FENCE = /^[ \t]*```mermaid[ \t]*\n([\s\S]*?)\n[ \t]*```[ \t]*$/gm
// A mermaid fence; any other fence, which stays markdown (an image link in it is
// code); or a line that is only an image link to an SVG next to the doc.
const BLOCKS =
  /^[ \t]*```mermaid[ \t]*\n([\s\S]*?)\n[ \t]*```[ \t]*$|^[ \t]*(`{3,}|~{3,})[^\n]*\n[\s\S]*?\n[ \t]*\2[ \t]*$|^[ \t]*!\[([^\]\n]*)\]\((?:\.\/)?([^)\s/\\:]+\.svg)\)[ \t]*$/gim

export function splitChunks(body: string): Chunk[] {
  const chunks: Chunk[] = []
  const lineAt = (offset: number) => body.slice(0, offset).split('\n').length
  let lastIndex = 0
  let m: RegExpExecArray | null
  BLOCKS.lastIndex = 0
  while ((m = BLOCKS.exec(body)) !== null) {
    if (m[2] !== undefined) continue
    if (m.index > lastIndex) {
      chunks.push({ kind: 'md', text: body.slice(lastIndex, m.index), line: lineAt(lastIndex) })
    }
    chunks.push(
      m[4] !== undefined
        ? { kind: 'svg', text: m[4], alt: m[3] ?? '', line: lineAt(m.index) }
        : { kind: 'mermaid', text: m[1] ?? '', line: lineAt(m.index) },
    )
    lastIndex = m.index + m[0].length
  }
  if (lastIndex < body.length) {
    chunks.push({ kind: 'md', text: body.slice(lastIndex), line: lineAt(lastIndex) })
  }
  if (chunks.length === 0) chunks.push({ kind: 'md', text: body, line: 1 })
  return chunks
}

/** `body` with the code of its `fence`-th (1-based) mermaid fence replaced; unchanged when there's no such fence. */
export function replaceFence(body: string, fence: number, code: string): string {
  let n = 0
  MERMAID_FENCE.lastIndex = 0
  let m: RegExpExecArray | null
  while ((m = MERMAID_FENCE.exec(body)) !== null) {
    if (++n < fence) continue
    const start = m.index + m[0].indexOf('\n') + 1
    return body.slice(0, start) + code + body.slice(start + m[1]!.length)
  }
  return body
}
