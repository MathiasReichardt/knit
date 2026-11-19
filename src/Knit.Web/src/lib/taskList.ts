// A GFM task-list item: indentation, a list marker (`-`/`*`/`+` or an
// ordered `1.` / `1)`), required whitespace, then a `[ ]` / `[x]` / `[X]`
// checkbox that is itself followed by whitespace or end-of-line. Capture
// groups: 1 = everything up to and including the opening `[`, 2 = the
// single state char, so the closing `]` sits at `prefix.length + 1`.
const TASK_RE = /^(\s*(?:[-*+]|\d+[.)])\s+\[)([ xX])\](?=\s|$)/

// A fenced code block delimiter (``` or ~~~, three or more). Task-list
// syntax inside a fence is literal text, not a checkbox — skip it.
const FENCE_RE = /^\s*(`{3,}|~{3,})/

/**
 * Flip the Nth GFM task-list checkbox in a markdown body and return the
 * rewritten body. `index` is zero-based and counts task-list items in
 * document order — the same order `MarkdownRender` emits `<input>`s, so
 * the Nth rendered checkbox maps to the Nth token here.
 *
 * Mirrors what the renderer actually turns into a checkbox: a leading YAML
 * frontmatter block and fenced code blocks are skipped (neither produces a
 * checkbox), `[ ]`/`[x]`/`[X]` and nested lists are recognised, and any
 * other list item is ignored. Returns the body unchanged if `index` is out
 * of range.
 */
export function toggleTaskListItem(body: string, index: number): string {
  const lines = (body ?? '').split('\n')
  let scanFrom = 0
  // Skip a leading frontmatter block the same way renderMarkdown does, so a
  // `- [ ]` inside YAML can't be miscounted as a body checkbox.
  if (lines[0]?.trimEnd() === '---') {
    for (let i = 1; i < lines.length; i++) {
      if (lines[i]?.trimEnd() === '---') {
        scanFrom = i + 1
        break
      }
    }
  }

  let fence: { char: string; len: number } | null = null
  let n = -1
  for (let i = scanFrom; i < lines.length; i++) {
    const line = lines[i]!
    const fm = FENCE_RE.exec(line)
    if (fm) {
      const marker = fm[1]!
      const char = marker[0]!
      const len = marker.length
      if (!fence) fence = { char, len }
      else if (char === fence.char && len >= fence.len) fence = null
      continue
    }
    if (fence) continue
    const tm = TASK_RE.exec(line)
    if (!tm) continue
    n++
    if (n === index) {
      const prefix = tm[1]!
      const flipped = tm[2] === ' ' ? 'x' : ' '
      lines[i] = prefix + flipped + line.slice(prefix.length + 1)
      break
    }
  }
  return lines.join('\n')
}
