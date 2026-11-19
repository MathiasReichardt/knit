import type { ComponentType } from 'react'
import { Check, Copy, FileText, Hash, Link2, Quote, Tag } from 'lucide-react'
import { useCopy } from '../hooks/useCopy'

interface IconProps {
  value: string
  title?: string
  className?: string
}

function makeCopyButton(DefaultIcon: ComponentType<{ className?: string }>, defaultTitle: string) {
  return function CopyButton({ value, title = defaultTitle, className = '' }: IconProps) {
    const { copied, copy } = useCopy(value)
    const disabled = !value
    return (
      <button
        type="button"
        onClick={copy}
        disabled={disabled}
        title={disabled ? `${title} (unavailable)` : copied ? 'Copied' : title}
        aria-label={title}
        className={
          'p-1 rounded shrink-0 ' +
          (disabled
            ? 'text-zinc-300 dark:text-zinc-700 cursor-not-allowed '
            : 'text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 ') +
          className
        }
      >
        {copied
          ? <Check className="h-3 w-3 text-green-600 dark:text-green-400" />
          : <DefaultIcon className="h-3 w-3" />}
      </button>
    )
  }
}

export const CopyIdIconButton = makeCopyButton(Hash, 'Copy id')
export const CopyPathIconButton = makeCopyButton(FileText, 'Copy file path')
export const CopySlugIconButton = makeCopyButton(Tag, 'Copy project slug')
// A cited ref for body text: `item:<full-id>` "Title" — so humans cite
// without typing the 18-char id.
export const CopyRefIconButton = makeCopyButton(Quote, 'Copy ref')
// The roadmap-relative markdown link to a milestone (milestones/<slug>.md) —
// paste target for roadmap prose.
export const CopyLinkIconButton = makeCopyButton(Link2, 'Copy link')
// A ready-to-run CLI command — the Doctor's way out of a config problem the
// SPA deliberately can't write (mounts).
// The plain copy glyph: the command is already on screen beside it, so the
// button's job is the copy, not saying "this is a terminal thing".
export const CopyCommandIconButton = makeCopyButton(Copy, 'Copy command')

