import { describe, it, expect } from 'vitest'
import { toggleTaskListItem } from './taskList'

describe('toggleTaskListItem', () => {
  it('checks an unchecked item', () => {
    expect(toggleTaskListItem('- [ ] one', 0)).toBe('- [x] one')
  })

  it('unchecks a checked item (lowercase and uppercase)', () => {
    expect(toggleTaskListItem('- [x] one', 0)).toBe('- [ ] one')
    expect(toggleTaskListItem('- [X] one', 0)).toBe('- [ ] one')
  })

  it('flips only the targeted index, leaving siblings untouched', () => {
    const body = '- [ ] a\n- [x] b\n- [ ] c'
    expect(toggleTaskListItem(body, 1)).toBe('- [ ] a\n- [ ] b\n- [ ] c')
  })

  it('counts nested task items in document order', () => {
    const body = '- [ ] top\n  - [ ] nested\n- [ ] bottom'
    expect(toggleTaskListItem(body, 1)).toBe('- [ ] top\n  - [x] nested\n- [ ] bottom')
  })

  it('supports `*`, `+`, and ordered list markers', () => {
    expect(toggleTaskListItem('* [ ] a', 0)).toBe('* [x] a')
    expect(toggleTaskListItem('+ [ ] a', 0)).toBe('+ [x] a')
    expect(toggleTaskListItem('1. [ ] a', 0)).toBe('1. [x] a')
    expect(toggleTaskListItem('1) [ ] a', 0)).toBe('1) [x] a')
  })

  it('ignores non-task list items when counting', () => {
    const body = '- plain bullet\n- [ ] real task'
    expect(toggleTaskListItem(body, 0)).toBe('- plain bullet\n- [x] real task')
  })

  it('ignores task-like lines inside a fenced code block', () => {
    const body = '```\n- [ ] not a task\n```\n- [ ] real task'
    expect(toggleTaskListItem(body, 0)).toBe('```\n- [ ] not a task\n```\n- [x] real task')
  })

  it('handles `~~~` fences too', () => {
    const body = '~~~\n- [ ] fenced\n~~~\n- [ ] real'
    expect(toggleTaskListItem(body, 0)).toBe('~~~\n- [ ] fenced\n~~~\n- [x] real')
  })

  it('skips a leading YAML frontmatter block', () => {
    const body = '---\ntags:\n  - [ ] looks-like-task\n---\n- [ ] body task'
    expect(toggleTaskListItem(body, 0)).toBe(
      '---\ntags:\n  - [ ] looks-like-task\n---\n- [x] body task',
    )
  })

  it('preserves trailing text after the checkbox', () => {
    expect(toggleTaskListItem('- [ ] do the thing (urgent)', 0)).toBe(
      '- [x] do the thing (urgent)',
    )
  })

  it('toggles a bare (empty) checkbox item', () => {
    expect(toggleTaskListItem('- [ ]', 0)).toBe('- [x]')
    expect(toggleTaskListItem('- [x]', 0)).toBe('- [ ]')
  })

  it('counts empty and filled task items together, in order', () => {
    const body = '- [ ]\n- [x] foo\n- [ ]'
    expect(toggleTaskListItem(body, 2)).toBe('- [ ]\n- [x] foo\n- [x]')
  })

  it('returns the body unchanged when the index is out of range', () => {
    const body = '- [ ] only one'
    expect(toggleTaskListItem(body, 5)).toBe(body)
  })

  it('preserves CRLF line endings on untouched lines', () => {
    const body = '- [ ] a\r\n- [ ] b'
    expect(toggleTaskListItem(body, 1)).toBe('- [ ] a\r\n- [x] b')
  })
})
