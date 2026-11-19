import { useTheme, type Theme } from '../stores/theme'

const OPTIONS: { value: Theme; label: string; glyph: string }[] = [
  { value: 'light', label: 'Light', glyph: '☀' },
  { value: 'system', label: 'System', glyph: '◐' },
  { value: 'dark', label: 'Dark', glyph: '☾' },
]

export function ThemeToggle() {
  const theme = useTheme((s) => s.theme)
  const setTheme = useTheme((s) => s.setTheme)

  return (
    <div
      role="radiogroup"
      aria-label="Theme"
      className="inline-flex rounded-md border border-zinc-300 dark:border-zinc-700 overflow-hidden text-sm"
    >
      {OPTIONS.map((opt) => {
        const active = theme === opt.value
        return (
          <button
            key={opt.value}
            type="button"
            role="radio"
            aria-checked={active}
            aria-label={opt.label}
            title={opt.label}
            onClick={() => setTheme(opt.value)}
            className={
              'px-2 py-1 transition-colors ' +
              (active
                ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
                : 'bg-transparent text-zinc-600 dark:text-zinc-400 hover:bg-zinc-100 dark:hover:bg-zinc-800')
            }
          >
            <span aria-hidden>{opt.glyph}</span>
          </button>
        )
      })}
    </div>
  )
}
