import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { InlineValue } from './InlineValue'
import { InlinePopover } from './InlinePopover'
import { claimEditor, releaseEditor } from './activeEditor'
import { textKey } from '../../lib/inlineKeys'
import { handleSuggestions } from '../../lib/handleVocabulary'

interface Props {
  value: string | null | undefined
  /** Field name for the `aria-label`s, e.g. `assignee`. */
  label: string
  onCommit: (next: string) => void
  /** Refuse an empty commit and keep the editor open (titles, slugs). */
  required?: boolean
  /** Shown in place of an empty value. Defaults to an em dash. */
  emptyDisplay?: React.ReactNode
  disabled?: boolean
  className?: string
  /** Typography for the input; defaults to inheriting the value's. */
  inputClassName?: string
  /**
   * Name the trigger by the value itself rather than "Edit <label>". For a
   * title inside an `<h1>`, whose accessible name must remain the title.
   */
  nameFromContent?: boolean
  /** Vocabulary to autocomplete against. Omit for a plain input. */
  suggestions?: readonly string[]
}

/**
 * A value that turns into a borderless in-place input. No panel and
 * no box: the caret lands in the same typography the value was rendered in, so
 * the only thing that changes is that you can type.
 *
 * Enter commits, blur commits, **Esc discards and restores the original**.
 * Esc has to suppress the commit the ensuing blur would otherwise fire —
 * without that guard, escaping out of an edit saves it, which is the opposite
 * of what Esc means everywhere else.
 *
 * Opening another editor displaces this one (see `activeEditor`) by blurring
 * the input, so being displaced commits exactly as clicking anywhere else
 * does — one path, not a second set of rules.
 *
 * With `suggestions` the input becomes a combobox, on the tag input's contract:
 * nothing is highlighted until ↓/↑, so Enter takes what was typed
 * unless a suggestion was chosen, and Esc drops a highlight before it reverts.
 */
export function InlineText({
  value,
  label,
  onCommit,
  required = false,
  emptyDisplay = <span className="text-zinc-400">—</span>,
  disabled = false,
  className = '',
  inputClassName = '',
  nameFromContent = false,
  suggestions,
}: Props) {
  const current = value ?? ''
  const id = useId()
  const triggerRef = useRef<HTMLButtonElement>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)
  // State as well as the ref: the popover mounts in the same render as the
  // input and needs its anchor then, when a plain ref would still be null.
  const [inputEl, setInputEl] = useState<HTMLInputElement | null>(null)
  const attachInput = useCallback((el: HTMLInputElement | null) => {
    inputRef.current = el
    setInputEl(el)
  }, [])
  const revertingRef = useRef(false)
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState('')
  const [invalid, setInvalid] = useState(false)
  const [active, setActive] = useState(-1)
  // Stable identity: the registry compares handles, and blurring routes the
  // displacement through the one commit path onBlur already owns.
  const handleRef = useRef({ close: () => inputRef.current?.blur() })

  useEffect(() => {
    if (!editing) return
    claimEditor(handleRef.current)
    const handle = handleRef.current
    return () => releaseEditor(handle)
  }, [editing])

  const matches = suggestions && editing ? handleSuggestions(suggestions, draft) : []
  const listOpen = matches.length > 0

  const open = () => {
    setDraft(current)
    setInvalid(false)
    setActive(-1)
    setEditing(true)
  }

  const closeToTrigger = () => {
    // Only reclaim focus when it is still ours. Doing it unconditionally stole
    // focus back a frame after a click had already moved it to another field.
    const keepFocus = inputRef.current !== null && document.activeElement === inputRef.current
    setEditing(false)
    setInvalid(false)
    if (keepFocus) requestAnimationFrame(() => triggerRef.current?.focus())
  }

  const revert = () => {
    revertingRef.current = true
    setDraft(current)
    closeToTrigger()
  }

  const commit = (raw: string = draft) => {
    const next = raw.trim()
    if (required && !next) {
      // Nothing is written and the editor stays put — a required field has no
      // "cleared" state, so silently reverting would look like a lost edit.
      setInvalid(true)
      return
    }
    closeToTrigger()
    if (next !== current) onCommit(next)
  }

  const onListKey = (e: React.KeyboardEvent<HTMLInputElement>): boolean => {
    if (!listOpen) return false
    if (e.key === 'ArrowDown') setActive((i) => (i >= matches.length - 1 ? 0 : i + 1))
    else if (e.key === 'ArrowUp') setActive((i) => (i <= 0 ? matches.length - 1 : i - 1))
    else if (e.key === 'Enter' && active >= 0) commit(matches[active] ?? draft)
    else if (e.key === 'Escape' && active >= 0) setActive(-1)
    else return false
    e.preventDefault()
    return true
  }

  if (editing) {
    const input = (
      <input
        ref={attachInput}
        type="text"
        autoFocus
        value={draft}
        readOnly={disabled}
        aria-label={label}
        aria-invalid={invalid || undefined}
        role={suggestions ? 'combobox' : undefined}
        aria-expanded={suggestions ? listOpen : undefined}
        aria-autocomplete={suggestions ? 'list' : undefined}
        aria-controls={listOpen ? `${id}-list` : undefined}
        aria-activedescendant={listOpen && active >= 0 ? `${id}-${active}` : undefined}
        onChange={(e) => { setDraft(e.target.value); setInvalid(false); setActive(-1) }}
        onBlur={() => {
          // Esc already closed the editor; the blur it caused must not save.
          if (revertingRef.current) { revertingRef.current = false; return }
          if (!disabled) commit()
        }}
        onKeyDown={(e) => {
          if (onListKey(e)) return
          const action = textKey(e.key)
          if (action.kind === 'none') return
          e.preventDefault()
          if (action.kind === 'commit') commit()
          else revert()
        }}
        className={
          'w-full min-w-0 bg-transparent px-1 -mx-1 rounded border-0 focus:outline-none '
          + (invalid
            ? 'ring-1 ring-red-500 '
            : 'ring-1 ring-blue-500 ')
          + inputClassName
        }
      />
    )
    if (!listOpen) return input
    return (
      <>
        {input}
        <InlinePopover anchor={inputEl} onClose={() => setActive(-1)} className="py-1">
          <div id={`${id}-list`} role="listbox" aria-label={`${label} suggestions`} className="max-h-48 min-w-[8rem] overflow-auto">
            {matches.map((m, i) => (
              <div
                key={m}
                id={`${id}-${i}`}
                role="option"
                aria-selected={i === active}
                // Keep focus on the input: a blur here would commit the raw
                // draft and race the click.
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => commit(m)}
                onMouseEnter={() => setActive(i)}
                className={'cursor-pointer px-2 py-1 text-sm ' + (i === active ? 'bg-zinc-100 dark:bg-zinc-800' : '')}
              >
                {m}
              </div>
            ))}
          </div>
        </InlinePopover>
      </>
    )
  }

  return (
    <InlineValue
      ref={triggerRef}
      label={label}
      verb="Edit"
      haspopup="dialog"
      onOpen={open}
      onKeyDown={(e) => {
        // Not `opensEditor`: Space and the arrows belong to a listbox, and a
        // text field that swallowed Space would be unusable once open.
        if (e.key === 'Enter') { e.preventDefault(); open() }
      }}
      disabled={disabled}
      nameFromContent={nameFromContent}
      className={className}
    >
      <span className="truncate">{current || emptyDisplay}</span>
    </InlineValue>
  )
}
