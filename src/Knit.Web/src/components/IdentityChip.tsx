import { useQuery } from '@tanstack/react-query'
import { api, qk } from '../api/client'

export function IdentityChip() {
  const q = useQuery({ queryKey: qk.identity, queryFn: api.identity })
  const handle = q.data?.handle ?? '(no identity)'
  const display = q.data?.display

  return (
    <div
      className="inline-flex items-center gap-2 rounded-full border border-zinc-300 dark:border-zinc-700 px-2.5 py-1 text-xs text-zinc-700 dark:text-zinc-300"
      title={display ? `${display} <${handle}>` : handle}
    >
      <span className="h-5 w-5 rounded-full bg-zinc-200 dark:bg-zinc-800 inline-flex items-center justify-center text-[10px] font-semibold uppercase">
        {initial(display || handle)}
      </span>
      <span className="font-mono truncate max-w-[10rem]">{handle}</span>
    </div>
  )
}

function initial(s: string): string {
  const t = s.trim()
  if (!t) return '?'
  return t[0].toUpperCase()
}
