import { RefreshCw, WifiOff } from 'lucide-react'
import { useConnection } from '../stores/connection'

export function ConnectionBanner() {
  const status = useConnection((s) => s.status)
  const checking = useConnection((s) => s.checking)
  const reconnectNow = useConnection((s) => s.reconnectNow)

  if (status !== 'disconnected') return null

  return (
    <div
      role="alert"
      className="flex items-center gap-2 border-b border-red-200 bg-red-50 px-4 py-2 text-sm text-red-800 dark:border-red-900/50 dark:bg-red-950/40 dark:text-red-300"
    >
      <WifiOff className="h-4 w-4 shrink-0" aria-hidden />
      <span className="flex-1">Disconnected — the Knit backend is unreachable.</span>
      <button
        type="button"
        onClick={() => reconnectNow?.()}
        disabled={checking || !reconnectNow}
        className="inline-flex items-center gap-1.5 rounded-md bg-red-100 px-2 py-1 font-medium hover:bg-red-200 disabled:opacity-60 dark:bg-red-900/40 dark:hover:bg-red-900/60"
      >
        <RefreshCw className={'h-3.5 w-3.5 ' + (checking ? 'animate-spin' : '')} aria-hidden />
        {checking ? 'Checking…' : 'Check now'}
      </button>
    </div>
  )
}
