import { useEffect, useState } from 'react'
import { Check, Share2 } from 'lucide-react'
import { shareApi, type ShareOrigin } from '../api/share'
import { forgeCommands } from '../lib/shareLink'
import { useToasts } from '../stores/toasts'
import { CopyCommandIconButton } from './CopyButton'
import { InlinePopover } from './inline/InlinePopover'

const TITLE = 'Copy share link'

/**
 * The fourth copy button: the file on the forge's default branch, so a
 * coworker without knit can open it. The toast shows the whole link. An
 * unknown host opens a popover with the `knit config set` command per forge
 * kind — the SPA writes no config.
 */
export function CopyShareLinkButton({ path }: { path: string }) {
  const toast = useToasts((s) => s.push)
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null)
  const [unknown, setUnknown] = useState<ShareOrigin | null>(null)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    if (!unknown) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setUnknown(null) }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [unknown])

  const copy = async (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    if (unknown) {
      setUnknown(null)
      return
    }
    try {
      const link = await shareApi.link(path)
      if (link.url === null) {
        if (link.origin.kind === 'unknown_host') setUnknown(link.origin)
        else toast('error', link.origin.error ?? 'No share link')
        return
      }
      await navigator.clipboard.writeText(link.url)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1200)
      toast('success', link.origin.kind === 'forge'
        ? `Copied ${link.url} — on the default branch, so an unmerged file 404s`
        : `Copied ${link.url} — no forge origin, so this names the repo and path`)
    } catch (err) {
      toast('error', `Share link failed: ${err instanceof Error ? err.message : String(err)}`)
    }
  }

  return (
    <>
      <button
        ref={setAnchor}
        type="button"
        onClick={(e) => { void copy(e) }}
        disabled={!path}
        title={copied ? 'Copied' : TITLE}
        aria-label={TITLE}
        aria-expanded={unknown !== null}
        className="p-1 rounded shrink-0 text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 disabled:text-zinc-300 dark:disabled:text-zinc-700"
      >
        {copied
          ? <Check className="h-3 w-3 text-green-600 dark:text-green-400" />
          : <Share2 className="h-3 w-3" />}
      </button>
      {unknown?.host && (
        <InlinePopover anchor={anchor} onClose={() => setUnknown(null)} className="w-[30rem] max-w-[calc(100vw-1rem)] p-3 space-y-2 text-xs">
          <p className="text-zinc-700 dark:text-zinc-300">
            Knit doesn&apos;t know which forge runs <span className="font-mono">{unknown.host}</span>. Tell it once:
          </p>
          <ul className="space-y-1">
            {forgeCommands(unknown.host).map(({ label, command }) => (
              <li key={command} className="flex items-center gap-2">
                <span className="w-14 shrink-0 text-zinc-500 dark:text-zinc-400">{label}</span>
                <code className="min-w-0 flex-1 break-all select-all rounded bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 font-mono text-zinc-800 dark:text-zinc-200">
                  {command}
                </code>
                <CopyCommandIconButton value={command} />
              </li>
            ))}
          </ul>
          <p className="text-zinc-500 dark:text-zinc-400">
            Add <span className="font-mono">--global</span> for every repo on this machine, <span className="font-mono">--web &lt;url&gt;</span> when
            the host isn&apos;t the web address. Then copy again.
          </p>
        </InlinePopover>
      )}
    </>
  )
}
