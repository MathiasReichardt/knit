import { useId, useRef, useState } from 'react'
import { Plus, X } from 'lucide-react'
import { InlinePopover } from './InlinePopover'
import { tagSuggestions } from '../../lib/tagVocabulary'

interface Props {
  values: readonly string[]
  /** Singular field name, e.g. `tag` — drives every `aria-label`. */
  label: string
  onChange: (next: string[]) => void
  /** Vocabulary to autocomplete against. Omit for a plain input. */
  suggestions?: readonly string[]
  busy?: boolean
  emptyDisplay?: React.ReactNode
}

/**
 * The chips field the other inline editors were modelled on: each value carries its
 * own remove, and a `+` adds one — value-level editing with no pen, which is
 * why it needed no redesign. Unified here so the item, KB and skills lists
 * stop carrying three copies of it.
 *
 * With `suggestions` the input becomes a combobox over the tags already in
 * use. Tags have no registry, so the vocabulary is folded out of the
 * lists the SPA has already fetched — see `tagVocabulary`.
 */
export function InlineChips({
  values,
  label,
  onChange,
  suggestions,
  busy = false,
  emptyDisplay = <span className="text-xs italic text-zinc-400">none</span>,
}: Props) {
  const id = useId()
  // State, not a ref: the suggestion popover mounts in the same render as
  // this input, so a ref would still be null when it needs its anchor.
  const [inputEl, setInputEl] = useState<HTMLInputElement | null>(null)
  const cancellingRef = useRef(false)
  const [adding, setAdding] = useState(false)
  const [draft, setDraft] = useState('')
  // -1 means "nothing highlighted", so Enter takes what was typed. Defaulting
  // to the first suggestion would silently swap a brand-new tag for a similar
  // existing one.
  const [active, setActive] = useState(-1)

  const matches = suggestions ? tagSuggestions(suggestions, draft, values) : []
  const listOpen = adding && matches.length > 0

  const add = (raw: string) => {
    const v = raw.trim()
    setDraft('')
    setActive(-1)
    setAdding(false)
    if (!v) return
    // Case-insensitive, matching the server's normalize-on-write: re-adding a
    // tag in different case is a no-op after the round-trip.
    if (values.some((x) => x.toLowerCase() === v.toLowerCase())) return
    onChange([...values, v])
  }

  const cancel = () => {
    // Same guard as InlineText: closing the input blurs it, and blur adds.
    cancellingRef.current = true
    setDraft('')
    setActive(-1)
    setAdding(false)
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown' && matches.length > 0) {
      e.preventDefault()
      setActive((i) => (i >= matches.length - 1 ? 0 : i + 1))
      return
    }
    if (e.key === 'ArrowUp' && matches.length > 0) {
      e.preventDefault()
      setActive((i) => (i <= 0 ? matches.length - 1 : i - 1))
      return
    }
    if (e.key === 'Enter') {
      e.preventDefault()
      add(active >= 0 ? matches[active] ?? draft : draft)
      return
    }
    if (e.key === 'Escape') {
      e.preventDefault()
      // Esc drops a highlighted suggestion first, so escaping out of the list
      // leaves what was typed intact; a second Esc abandons the add.
      if (active >= 0) setActive(-1)
      else cancel()
    }
  }

  return (
    <div className="group flex flex-wrap items-center gap-1.5">
      {values.length === 0 && !adding && emptyDisplay}
      {values.map((t) => (
        <span
          key={t}
          className="inline-flex items-center gap-1 rounded-full border border-zinc-200 bg-zinc-100 px-2 py-0.5 font-mono text-[11px] dark:border-zinc-700 dark:bg-zinc-800"
        >
          <span>{t}</span>
          <button
            type="button"
            disabled={busy}
            onClick={() => onChange(values.filter((x) => x !== t))}
            title={`Remove ${label}`}
            aria-label={`Remove ${t}`}
            className="opacity-50 hover:text-red-600 hover:opacity-100"
          >
            <X className="h-3 w-3" />
          </button>
        </span>
      ))}
      {adding ? (
        <>
          <input
            ref={setInputEl}
            type="text"
            autoFocus
            value={draft}
            readOnly={busy}
            role={suggestions ? 'combobox' : undefined}
            aria-expanded={suggestions ? listOpen : undefined}
            aria-autocomplete={suggestions ? 'list' : undefined}
            aria-controls={listOpen ? `${id}-list` : undefined}
            aria-activedescendant={listOpen && active >= 0 ? `${id}-${active}` : undefined}
            aria-label={`Add ${label}`}
            placeholder={label}
            onChange={(e) => { setDraft(e.target.value); setActive(-1) }}
            onBlur={() => {
              if (cancellingRef.current) { cancellingRef.current = false; return }
              add(draft)
            }}
            onKeyDown={onKeyDown}
            className="w-32 rounded border border-zinc-300 bg-white px-1.5 py-0.5 font-mono text-[11px] dark:border-zinc-700 dark:bg-zinc-900"
          />
          {listOpen && (
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
                    onClick={() => add(m)}
                    onMouseEnter={() => setActive(i)}
                    className={
                      'cursor-pointer px-2 py-1 font-mono text-[11px] '
                      + (i === active ? 'bg-zinc-100 dark:bg-zinc-800' : '')
                    }
                  >
                    {m}
                  </div>
                ))}
              </div>
            </InlinePopover>
          )}
        </>
      ) : (
        <button
          type="button"
          onClick={() => { setDraft(''); setActive(-1); setAdding(true) }}
          title={`Add ${label}`}
          aria-label={`Add ${label}`}
          className="inline-flex items-center gap-0.5 rounded border border-dashed border-zinc-300 px-1.5 py-0.5 text-[11px] text-zinc-500 opacity-0 hover:bg-zinc-100 focus:opacity-100 group-hover:opacity-100 dark:border-zinc-700 dark:hover:bg-zinc-900"
        >
          <Plus className="h-3 w-3" />
        </button>
      )}
    </div>
  )
}
