import { forwardRef } from 'react'

interface Props {
  /** Field name in the imperative — becomes `Change state` / `Edit assignee`. */
  label: string
  /** 'Change' for a picker, 'Edit' for free text. */
  verb?: 'Change' | 'Edit'
  onOpen: () => void
  onKeyDown?: (e: React.KeyboardEvent<HTMLButtonElement>) => void
  open?: boolean
  disabled?: boolean
  /** True for a picker, so assistive tech announces the popup. */
  haspopup?: 'listbox' | 'dialog'
  /**
   * Name the button by its own text instead of `${verb} ${label}`. For a
   * heading, whose accessible name must stay the title — an `aria-label` there
   * would have a screen reader announce "Edit title" when navigating headings.
   */
  nameFromContent?: boolean
  children: React.ReactNode
  className?: string
}

/**
 * The value itself, as the button. Replaces the hover-pen: there is
 * no separate trigger to find, the field sits in the tab order on its own, and
 * the `aria-label` rides on the thing you actually click instead of on a pen
 * beside it.
 *
 * The hover / focus tint is the only affordance — no icon, no border — so a
 * read-only row and an editable one stay visually distinct without clutter.
 */
export const InlineValue = forwardRef<HTMLButtonElement, Props>(function InlineValue(
  { label, verb = 'Change', onOpen, onKeyDown, open = false, disabled = false, haspopup = 'listbox', nameFromContent = false, children, className = '' },
  ref,
) {
  return (
    <button
      ref={ref}
      type="button"
      disabled={disabled}
      onClick={onOpen}
      onKeyDown={onKeyDown}
      aria-label={nameFromContent ? undefined : `${verb} ${label}`}
      title={nameFromContent ? `${verb} ${label}` : undefined}
      aria-haspopup={haspopup}
      aria-expanded={open}
      className={
        'group/inline inline-flex max-w-full min-w-0 items-center gap-1.5 rounded px-1 -mx-1 text-left '
        + 'hover:bg-zinc-200/70 dark:hover:bg-zinc-800 '
        + 'focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500 '
        + 'disabled:pointer-events-none disabled:opacity-50 '
        + (open ? 'bg-zinc-200/70 dark:bg-zinc-800 ' : '')
        + className
      }
    >
      {children}
    </button>
  )
})
