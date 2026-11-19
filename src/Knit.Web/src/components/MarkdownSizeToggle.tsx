import {
  MARKDOWN_SIZES,
  MARKDOWN_SIZE_LABEL,
  useMarkdownSize,
  type MarkdownSize,
} from '../stores/markdownSize'

// Glyph size tracks the step so the control shows the ladder, not just labels.
const GLYPH_CLASS: Record<MarkdownSize, string> = {
  sm: 'text-[10px]',
  base: 'text-xs',
  lg: 'text-sm',
}

export function MarkdownSizeToggle() {
  const size = useMarkdownSize((s) => s.size)
  const setSize = useMarkdownSize((s) => s.setSize)

  return (
    <div
      role="radiogroup"
      aria-label="Markdown size"
      className="inline-flex rounded-md border border-zinc-300 dark:border-zinc-700 overflow-hidden"
    >
      {MARKDOWN_SIZES.map((opt) => {
        const active = size === opt
        return (
          <button
            key={opt}
            type="button"
            role="radio"
            aria-checked={active}
            aria-label={MARKDOWN_SIZE_LABEL[opt]}
            title={MARKDOWN_SIZE_LABEL[opt]}
            onClick={() => setSize(opt)}
            className={
              'px-2 py-1 leading-none transition-colors ' +
              GLYPH_CLASS[opt] +
              ' ' +
              (active
                ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
                : 'bg-transparent text-zinc-600 dark:text-zinc-400 hover:bg-zinc-100 dark:hover:bg-zinc-800')
            }
          >
            <span aria-hidden>A</span>
          </button>
        )
      })}
    </div>
  )
}
