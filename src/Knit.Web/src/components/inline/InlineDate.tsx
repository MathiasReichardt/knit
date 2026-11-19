import { useEffect, useRef, useState } from 'react'
import { CalendarClock, ChevronLeft, ChevronRight } from 'lucide-react'
import { InlinePopover } from './InlinePopover'
import { InlineValue } from './InlineValue'
import { claimEditor, releaseEditor } from './activeEditor'
import { opensEditor } from '../../lib/inlineKeys'
import { WEEKDAY_LABELS, addMonths, daysInMonth, isoDate, monthGrid, monthLabel, openMonth, parseIsoDate } from '../../lib/calendar'
import { todayIso } from '../../lib/dueStatus'

interface Props {
  value: string | null | undefined
  /** Field name for the `aria-label`s, e.g. `due date`. */
  label: string
  onChange: (next: string) => void
  /** Offer a Clear row (sends ''). */
  clearable?: boolean
  disabled?: boolean
  /** Trigger content. Defaults to a calendar glyph + the raw ISO date. */
  children?: React.ReactNode
  className?: string
  /** Injectable for tests; defaults to the local calendar day. */
  today?: string
}

/**
 * A value that opens an anchored month calendar. A date editor can
 * never fit in the value's footprint, so this is a popover by necessity rather
 * than by preference — and it is ours rather than `<input type="date">`
 * because that control's picker is OS-styled chrome the SPA cannot theme.
 *
 * Day cells are real buttons: arrows move DOM focus, Enter / Space activate
 * natively, so the grid needs no `aria-activedescendant` bookkeeping.
 */
export function InlineDate({
  value,
  label,
  onChange,
  clearable = true,
  disabled = false,
  children,
  className = '',
  today = todayIso(),
}: Props) {
  // State, not a ref: the popover needs the trigger element during render,
  // and a ref's value is not readable there.
  const [triggerEl, setTriggerEl] = useState<HTMLButtonElement | null>(null)
  const gridRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  // Opening another field closes this one too — one editor at a time
  // (see `activeEditor`). setOpen is stable, so the handle can be.
  const handleRef = useRef({ close: () => setOpen(false) })
  // The focused day is the single source of truth — the month on screen is
  // derived from it. Keeping them as separate state let a month page snap
  // straight back to the focused day's month.
  const [focusDate, setFocusDate] = useState(() => value || today)
  const month = openMonth(focusDate, today)

  const openAt = () => {
    setFocusDate(value || today)
    setOpen(true)
  }

  const close = (refocus = true) => {
    setOpen(false)
    if (refocus) triggerEl?.focus()
  }

  const pick = (iso: string) => {
    close()
    if (iso !== (value ?? '')) onChange(iso)
  }

  useEffect(() => {
    if (!open) return
    claimEditor(handleRef.current)
    const handle = handleRef.current
    return () => releaseEditor(handle)
  }, [open])

  // Put DOM focus on the focused day's cell; arrow keys that cross a month
  // boundary carry the grid with them, because the month follows focusDate.
  useEffect(() => {
    if (!open) return
    const cell = gridRef.current?.querySelector<HTMLButtonElement>(`[data-date="${focusDate}"]`)
    if (cell) cell.focus()
    else gridRef.current?.focus()
  }, [open, focusDate])

  const shift = (days: number) => {
    const parsed = parseIsoDate(focusDate) ?? parseIsoDate(today)
    if (!parsed) return
    const dt = new Date(Date.UTC(parsed.year, parsed.month - 1, parsed.day))
    dt.setUTCDate(dt.getUTCDate() + days)
    setFocusDate(dt.toISOString().slice(0, 10))
  }

  // Paging a month keeps the day where it is, clamped — 31 Mar back a month is
  // 28 Feb, not 3 Mar.
  const page = (delta: number) => {
    const parsed = parseIsoDate(focusDate) ?? parseIsoDate(today)
    if (!parsed) return
    const next = addMonths({ year: parsed.year, month: parsed.month }, delta)
    setFocusDate(isoDate(next, Math.min(parsed.day, daysInMonth(next))))
  }

  const onGridKeyDown = (e: React.KeyboardEvent) => {
    switch (e.key) {
      case 'ArrowLeft': shift(-1); break
      case 'ArrowRight': shift(1); break
      case 'ArrowUp': shift(-7); break
      case 'ArrowDown': shift(7); break
      case 'PageUp': page(-1); break
      case 'PageDown': page(1); break
      case 'Escape': close(); break
      default: return
    }
    e.preventDefault()
  }

  const parsedValue = parseIsoDate(value)
  return (
    <>
      <InlineValue
        ref={setTriggerEl}
        label={label}
        onOpen={openAt}
        onKeyDown={(e) => { if (opensEditor(e.key)) { e.preventDefault(); openAt() } }}
        open={open}
        disabled={disabled}
        haspopup="dialog"
        className={className}
      >
        {children ?? (
          <>
            <CalendarClock aria-hidden className="h-3.5 w-3.5 shrink-0" />
            <span className="truncate">{value || '—'}</span>
          </>
        )}
      </InlineValue>
      {open && (
        <InlinePopover anchor={triggerEl} onClose={() => close(false)} className="p-2">
          <div role="dialog" aria-label={label} className="flex w-[15rem] flex-col gap-1">
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={() => page(-1)}
                aria-label="Previous month"
                className="rounded p-1 text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800"
              >
                <ChevronLeft className="h-3.5 w-3.5" />
              </button>
              <span aria-live="polite" className="flex-1 text-center text-xs font-medium">
                {monthLabel(month)}
              </span>
              <button
                type="button"
                onClick={() => page(1)}
                aria-label="Next month"
                className="rounded p-1 text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800"
              >
                <ChevronRight className="h-3.5 w-3.5" />
              </button>
            </div>
            <div
              ref={gridRef}
              role="grid"
              tabIndex={-1}
              aria-label={label}
              onKeyDown={onGridKeyDown}
              className="focus:outline-none"
            >
              <div role="row" className="grid grid-cols-7">
                {WEEKDAY_LABELS.map((d) => (
                  <span
                    key={d}
                    role="columnheader"
                    className="py-0.5 text-center text-[10px] uppercase text-zinc-400"
                  >
                    {d}
                  </span>
                ))}
              </div>
              {monthGrid(month).map((week, w) => (
                <div role="row" key={w} className="grid grid-cols-7">
                  {week.map((iso, d) => {
                    if (!iso) return <span role="gridcell" key={`${w}-${d}`} />
                    const day = Number(iso.slice(8))
                    const selected = parsedValue !== null && iso === isoDate(parsedValue, parsedValue.day)
                    const isToday = iso === today
                    return (
                      <button
                        key={iso}
                        type="button"
                        role="gridcell"
                        data-date={iso}
                        tabIndex={iso === focusDate ? 0 : -1}
                        aria-selected={selected}
                        aria-label={iso}
                        onClick={() => pick(iso)}
                        className={
                          'rounded py-0.5 text-center text-xs focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500 '
                          + (selected
                            ? 'bg-blue-600 text-white '
                            : isToday
                              ? 'font-semibold text-blue-600 hover:bg-zinc-100 dark:hover:bg-zinc-800 '
                              : 'hover:bg-zinc-100 dark:hover:bg-zinc-800 ')
                        }
                      >
                        {day}
                      </button>
                    )
                  })}
                </div>
              ))}
            </div>
            <div className="flex items-center gap-1 border-t border-zinc-200 pt-1 dark:border-zinc-700">
              <button
                type="button"
                onClick={() => pick(today)}
                className="rounded px-1.5 py-0.5 text-xs text-zinc-600 hover:bg-zinc-100 dark:text-zinc-300 dark:hover:bg-zinc-800"
              >
                Today
              </button>
              {clearable && value && (
                <button
                  type="button"
                  onClick={() => pick('')}
                  className="ml-auto rounded px-1.5 py-0.5 text-xs text-zinc-600 hover:bg-zinc-100 hover:text-red-600 dark:text-zinc-300 dark:hover:bg-zinc-800"
                >
                  Clear
                </button>
              )}
            </div>
          </div>
        </InlinePopover>
      )}
    </>
  )
}
