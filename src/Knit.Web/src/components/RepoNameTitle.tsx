import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil } from 'lucide-react'
import { repoApi, repoQk } from '../api/repo'
import { configQk } from '../api/config'
import { useToasts } from '../stores/toasts'

/** The repo's name, centred in the header; editable in place, saved as `repo_name`. */
export function RepoNameTitle() {
  const name = useQuery({ queryKey: repoQk.name, queryFn: repoApi.name })
  const client = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [draft, setDraft] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)

  const current = name.data?.name

  useEffect(() => {
    document.title = current ? `${current} · Knit` : 'Knit'
  }, [current])

  const editing = draft !== null
  useEffect(() => {
    if (editing) input.current?.select()
  }, [editing])

  const save = useMutation({
    mutationFn: repoApi.setName,
    onSuccess: (data) => {
      client.setQueryData(repoQk.name, data)
      client.invalidateQueries({ queryKey: configQk.get() })
    },
    onError: (e: Error) => toast('error', `Rename failed: ${e.message}`),
  })

  if (!name.data) return null

  const commit = () => {
    if (draft === null) return
    const next = draft.trim()
    setDraft(null)
    if (next === name.data.name || (next === '' && name.data.source === 'default')) return
    save.mutate(next)
  }

  if (draft !== null) {
    return (
      <input
        ref={input}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === 'Enter') commit()
          else if (e.key === 'Escape') setDraft(null)
        }}
        placeholder={name.data.default}
        aria-label="Repo name"
        title="Enter to save, Esc to cancel; empty resets to the folder name"
        className="w-64 h-8 rounded-md border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 px-2 text-base font-semibold text-center text-zinc-900 dark:text-zinc-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
      />
    )
  }

  return (
    // The pencil hangs off the right edge so the name itself stays centred.
    <div className="group relative flex items-center min-w-0">
      <h1
        className="truncate text-base font-semibold text-zinc-900 dark:text-zinc-100"
        title={name.data.source === 'default' ? `${name.data.name} (folder name)` : name.data.name}
      >
        {name.data.name}
      </h1>
      <button
        type="button"
        onClick={() => setDraft(name.data.name)}
        title="Rename repo"
        aria-label="Rename repo"
        className="absolute left-full ml-1 inline-flex items-center justify-center h-6 w-6 rounded text-zinc-500 dark:text-zinc-400 opacity-0 group-hover:opacity-100 focus:opacity-100 hover:bg-zinc-200/70 dark:hover:bg-zinc-800"
      >
        <Pencil className="h-3.5 w-3.5" aria-hidden />
      </button>
    </div>
  )
}
