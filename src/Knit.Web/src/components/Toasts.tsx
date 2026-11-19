import { useState } from 'react'
import { Terminal, X } from 'lucide-react'
import { useToasts, type Toast } from '../stores/toasts'
import { CopyCommandIconButton } from './CopyButton'
import { InlinePopover } from './inline/InlinePopover'

export function Toasts() {
  const toasts = useToasts((s) => s.toasts)
  const dismiss = useToasts((s) => s.dismiss)
  if (toasts.length === 0) return null
  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-md">
      {toasts.map((t) => (
        <div
          key={t.id}
          role="status"
          className={
            'flex items-start gap-2 rounded-md border px-3 py-2 text-sm shadow-md backdrop-blur ' +
            (t.kind === 'error'
              ? 'border-red-300 dark:border-red-800 bg-red-50/95 dark:bg-red-950/80 text-red-800 dark:text-red-200'
              : t.kind === 'success'
                ? 'border-emerald-300 dark:border-emerald-800 bg-emerald-50/95 dark:bg-emerald-950/80 text-emerald-800 dark:text-emerald-200'
                : 'border-zinc-300 dark:border-zinc-700 bg-white/95 dark:bg-zinc-900/95 text-zinc-800 dark:text-zinc-200')
          }
        >
          <span className="flex-1 break-words">{t.message}</span>
          {t.command && <ToastCommand command={t.command} />}
          <button
            type="button"
            onClick={() => dismiss(t.id)}
            title="Dismiss"
            aria-label="Dismiss notification"
            className="opacity-70 hover:opacity-100"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      ))}
    </div>
  )
}

/** The command a toast offers, in a click popover so it can be selected and copied. */
function ToastCommand({ command }: { command: NonNullable<Toast['command']> }) {
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null)
  const [open, setOpen] = useState(false)
  return (
    <>
      <button
        ref={setAnchor}
        type="button"
        onClick={() => setOpen((v) => !v)}
        title="Show the command"
        aria-label="Show the command"
        aria-expanded={open}
        className="opacity-70 hover:opacity-100"
      >
        <Terminal className="h-3.5 w-3.5" />
      </button>
      {open && (
        <InlinePopover anchor={anchor} onClose={() => setOpen(false)} className="max-w-[calc(100vw-1rem)] p-3 space-y-2 text-xs">
          <p className="text-zinc-700 dark:text-zinc-300">Run it in a terminal in this repo:</p>
          <div className="flex items-center gap-2">
            <code className="min-w-0 flex-1 break-all select-all rounded bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 font-mono text-zinc-800 dark:text-zinc-200">
              {command}
            </code>
            <CopyCommandIconButton value={command} />
          </div>
        </InlinePopover>
      )}
    </>
  )
}
