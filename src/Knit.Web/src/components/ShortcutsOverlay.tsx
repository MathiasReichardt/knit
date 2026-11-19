import { useEffect } from 'react'
import { useDialogs } from '../stores/dialogs'

interface Row {
  keys: string[]
  description: string
}

interface Group {
  title: string
  rows: Row[]
}

const GROUPS: Group[] = [
  {
    title: 'Global',
    rows: [
      { keys: ['?'], description: 'Show this overlay' },
      { keys: ['/', 'Ctrl+K'], description: 'Focus search' },
      { keys: ['Ctrl+Enter'], description: 'Open the search / jump pick in a new tab' },
      { keys: ['n'], description: 'New item' },
      { keys: ['m'], description: 'Assign selected item to me' },
      { keys: ['[', ']'], description: 'Hide / show the left or right panel' },
    ],
  },
  {
    title: 'Navigation',
    rows: [
      { keys: ['g', 'o'], description: 'Overview (landing page)' },
      { keys: ['g', 't'], description: 'Items — tree view' },
      { keys: ['g', 'l'], description: 'Items — list view' },
      { keys: ['g', 'b'], description: 'Knowledge base' },
      { keys: ['g', 'd'], description: 'Designs' },
      { keys: ['g', 'a'], description: 'Timeline (deadline agenda)' },
      { keys: ['g', 'h'], description: 'Doctor (repo health)' },
      { keys: ['g', 'r'], description: 'Jump to id or ref' },
      { keys: ['↑', '↓'], description: 'Previous / next (list or tree)' },
      { keys: ['←', '→'], description: 'Collapse / expand (tree)' },
      { keys: ['↑', '↓', '←', '→'], description: 'Move across cards (overview)' },
    ],
  },
  {
    title: 'Outline',
    rows: [
      { keys: ['o'], description: 'Open the outline of the text shown' },
      { keys: ['Ctrl+Shift+O', 'Alt+O'], description: 'Open the outline in the editor' },
      { keys: ['↑', '↓'], description: 'Jump to the previous / next section' },
      { keys: ['Home', 'End'], description: 'Jump to the first / last section' },
      { keys: ['c'], description: 'Copy the section link' },
      { keys: ['Esc'], description: 'Close, back to the text' },
    ],
  },
  {
    title: 'Editor',
    rows: [
      { keys: ['e'], description: 'Edit selected item / KB entry / design' },
      { keys: ['Ctrl+S'], description: 'Save' },
      { keys: ['Esc'], description: 'Leave the editor' },
    ],
  },
]

export function ShortcutsOverlay() {
  const open = useDialogs((s) => s.shortcutsOpen)
  const toggle = useDialogs((s) => s.toggleShortcuts)
  const close = useDialogs((s) => s.closeShortcuts)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === '?' && !isTypingTarget(e.target)) {
        e.preventDefault()
        toggle()
      } else if (e.key === 'Escape') {
        close()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [toggle, close])

  if (!open) return null
  return (
    <div
      role="dialog"
      aria-label="Keyboard shortcuts"
      aria-modal="true"
      onClick={() => close()}
      className="fixed inset-0 z-30 bg-black/40 flex items-center justify-center p-4"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="bg-white dark:bg-zinc-950 border border-zinc-200 dark:border-zinc-800 rounded-lg shadow-xl w-full max-w-md max-h-full overflow-y-auto p-5"
      >
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold uppercase tracking-wide text-zinc-700 dark:text-zinc-300">
            Keyboard shortcuts
          </h2>
          <button
            type="button"
            onClick={() => close()}
            className="text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100 text-sm"
            aria-label="Close"
          >
            ✕
          </button>
        </div>
        <div className="space-y-4">
          {GROUPS.map((g) => (
            <section key={g.title} aria-label={g.title}>
              <h3 className="mb-1.5 text-[11px] font-semibold uppercase tracking-wide text-zinc-500">{g.title}</h3>
              <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
                {g.rows.map((r) => (
                  <div key={r.description} className="contents">
                    <dt className="flex items-center gap-1">
                      {r.keys.map((k, i) => (
                        <kbd
                          key={i}
                          className="font-mono text-[11px] bg-zinc-100 dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-700 rounded px-1.5 py-0.5"
                        >
                          {k}
                        </kbd>
                      ))}
                    </dt>
                    <dd className="text-zinc-600 dark:text-zinc-400">{r.description}</dd>
                  </div>
                ))}
              </dl>
            </section>
          ))}
        </div>
      </div>
    </div>
  )
}

function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || target.isContentEditable
}
