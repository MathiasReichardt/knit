import { InlineText } from './inline/InlineText'

interface Props {
  value: string
  onCommit: (next: string) => void
  /** When false, no edit affordance is shown (e.g. adr titles). */
  editable?: boolean
  /** When false (default), committing an empty value is refused. */
  allowEmpty?: boolean
  /** Classes for the heading — the input inherits them, so the caret lands in
   * the same typography the title was rendered in. */
  className?: string
  ariaLabel?: string
  /** Anchor id on the view-mode wrapper (jump-to-occurrence title flash). */
  id?: string
}

/**
 * The item / KB / milestone title: a heading you click to edit (22). The
 * heading itself is the control — there is no pen beside it — and an empty
 * commit is refused rather than silently reverted, because a title has no
 * "cleared" state and a dropped edit looks like a lost one.
 *
 * The `<h1>` stays *outside* the button (a button's content model is phrasing
 * content) and the button is named by the title it shows, not by "Edit title",
 * so navigating by heading still reads the title.
 */
export function EditableHeading({
  value,
  onCommit,
  editable = true,
  allowEmpty = false,
  className = '',
  ariaLabel = 'title',
  id,
}: Props) {
  return (
    <h1 id={id} className={'flex min-w-0 flex-1 items-start ' + className}>
      {editable ? (
        <InlineText
          value={value}
          label={ariaLabel}
          required={!allowEmpty}
          nameFromContent
          onCommit={onCommit}
          inputClassName={className}
        />
      ) : (
        value
      )}
    </h1>
  )
}
