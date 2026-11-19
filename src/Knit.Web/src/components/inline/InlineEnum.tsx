import { useEffect, useId, useRef, useState } from 'react'
import { Check } from 'lucide-react'
import type { IconCmp } from '../../lib/glyphs'
import { InlinePopover } from './InlinePopover'
import { InlineValue } from './InlineValue'
import { claimEditor, releaseEditor } from './activeEditor'
import { listboxKey, opensEditor } from '../../lib/inlineKeys'

export interface InlineEnumOption<T extends string> {
  value: T
  label: string
  icon?: IconCmp
  iconClass?: string
  /** Heading rendered above this option; repeat it on each option in the group. */
  group?: string
}

interface Props<T extends string> {
  value: T
  options: readonly InlineEnumOption<T>[]
  /** Field name for the `aria-label`s, e.g. `state`. */
  label: string
  onChange: (next: T) => void
  disabled?: boolean
  /** Trigger content. Defaults to the current option's icon + label. */
  children?: React.ReactNode
  className?: string
}

/**
 * A value that opens an anchored listbox. Deliberately not a native
 * `<select>`: the options carry the same value-encoding icon and colour the
 * value does, which a native option list cannot render, and the OS-styled
 * dropdown is the one control in the SPA that ignores its theme.
 *
 * Option focus is visual only (`aria-activedescendant`) — DOM focus stays on
 * the listbox so arrowing never commits; only Enter / Space / Tab / a click do.
 */
export function InlineEnum<T extends string>({
  value,
  options,
  label,
  onChange,
  disabled = false,
  children,
  className = '',
}: Props<T>) {
  const id = useId()
  // State, not a ref: the popover needs the trigger element during render,
  // and a ref's value is not readable there.
  const [triggerEl, setTriggerEl] = useState<HTMLButtonElement | null>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  // Opening another field closes this one too — one editor at a time
  // (see `activeEditor`). setOpen is stable, so the handle can be.
  const handleRef = useRef({ close: () => setOpen(false) })
  const [active, setActive] = useState(0)

  const current = options.find((o) => o.value === value)

  const openAt = () => {
    const at = options.findIndex((o) => o.value === value)
    setActive(at < 0 ? 0 : at)
    setOpen(true)
  }

  const close = (refocus = true) => {
    setOpen(false)
    if (refocus) triggerEl?.focus()
  }

  const commit = (index: number) => {
    const picked = options[index]
    close()
    if (picked && picked.value !== value) onChange(picked.value)
  }

  // The listbox takes DOM focus on open so the arrow keys reach it without the
  // caller having to manage a focus trap.
  useEffect(() => { if (open) listRef.current?.focus() }, [open])

  useEffect(() => {
    if (!open) return
    claimEditor(handleRef.current)
    const handle = handleRef.current
    return () => releaseEditor(handle)
  }, [open])

  const onListKeyDown = (e: React.KeyboardEvent) => {
    const action = listboxKey(e.key, active, options.length)
    if (action.kind === 'none') return
    e.preventDefault()
    switch (action.kind) {
      case 'move': setActive(action.index); break
      case 'commit': commit(action.index); break
      // Tab commits and hands focus back to the field rather than guessing the
      // next tabbable across the portal boundary — the following Tab then moves
      // on from a predictable place.
      case 'commitAndLeave': commit(action.index); break
      case 'cancel': close(); break
    }
  }

  // A group heading renders above the first option carrying it; derived up
  // front rather than tracked through the map, which would be a write during
  // render.
  const rows = options.map((o, i) => ({
    option: o,
    heading: o.group && o.group !== options[i - 1]?.group ? o.group : null,
  }))

  return (
    <>
      <InlineValue
        ref={setTriggerEl}
        label={label}
        onOpen={openAt}
        onKeyDown={(e) => { if (opensEditor(e.key)) { e.preventDefault(); openAt() } }}
        open={open}
        disabled={disabled}
        className={className}
      >
        {children ?? (
          <>
            {current?.icon && (
              <current.icon aria-hidden className={(current.iconClass ?? '') + ' h-4 w-4 shrink-0'} />
            )}
            <span className="truncate">{current?.label ?? value}</span>
          </>
        )}
      </InlineValue>
      {open && (
        <InlinePopover anchor={triggerEl} onClose={() => close(false)} className="py-1 min-w-[10rem]">
          <div
            ref={listRef}
            role="listbox"
            tabIndex={-1}
            aria-label={label}
            aria-activedescendant={options[active] ? `${id}-${active}` : undefined}
            onKeyDown={onListKeyDown}
            className="max-h-72 overflow-auto focus:outline-none"
          >
            {rows.map(({ option: o, heading }, i) => {
              const selected = o.value === value
              return (
                <div key={o.value || `${id}-empty`}>
                  {heading && (
                    <div className="px-2 pt-1.5 pb-0.5 text-[10px] uppercase tracking-wide text-zinc-400">
                      {heading}
                    </div>
                  )}
                  <div
                    id={`${id}-${i}`}
                    role="option"
                    aria-selected={selected}
                    onClick={() => commit(i)}
                    onMouseEnter={() => setActive(i)}
                    className={
                      'flex cursor-pointer items-center gap-1.5 px-2 py-1 text-xs '
                      + (i === active ? 'bg-zinc-100 dark:bg-zinc-800 ' : '')
                    }
                  >
                    {o.icon
                      ? <o.icon aria-hidden className={(o.iconClass ?? '') + ' h-3.5 w-3.5 shrink-0'} />
                      : <span aria-hidden className="h-3.5 w-3.5 shrink-0" />}
                    <span className="truncate">{o.label}</span>
                    {selected && <Check aria-hidden className="ml-auto h-3 w-3 shrink-0 text-blue-600" />}
                  </div>
                </div>
              )
            })}
          </div>
        </InlinePopover>
      )}
    </>
  )
}
