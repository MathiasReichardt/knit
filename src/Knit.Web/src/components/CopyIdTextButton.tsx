import { Check } from 'lucide-react'
import { useCopy } from '../hooks/useCopy'

// The shown short id as its own copy button (the "show ids" view toggle) —
// copies the full id, since a short id can grow once another item shares its
// prefix.
export function CopyIdTextButton({
  id,
  label,
  className = '',
  style,
}: {
  id: string
  label: string
  className?: string
  style?: React.CSSProperties
}) {
  const { copied, copy } = useCopy(id)
  return (
    <button
      type="button"
      onClick={copy}
      // Rows toggle on double-click; a double-click here is two copies, not a toggle.
      onDoubleClick={(e) => e.stopPropagation()}
      title={copied ? 'Copied' : 'Copy full id'}
      style={style}
      aria-label={`Copy full id (${label})`}
      className={
        'inline-flex items-center shrink-0 rounded font-mono text-left truncate ' +
        'hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 ' +
        className
      }
    >
      {copied ? <Check className="h-3 w-3 text-green-600 dark:text-green-400" /> : label}
    </button>
  )
}
