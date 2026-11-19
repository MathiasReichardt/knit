import { useEffect, useRef, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { BookOpenText, FileText, Keyboard, Rows3 } from 'lucide-react'
import { ThemeToggle } from './ThemeToggle'
import { MarkdownSizeToggle } from './MarkdownSizeToggle'
import { VersionIndicator } from './RepoIndicator'
import { useDialogs } from '../stores/dialogs'
import { useStatusPage, useWriteStatusReport } from '../hooks/useStatusPage'
import { StaleDot } from './StaleDot'

export function HamburgerMenu() {
  const [open, setOpen] = useState(false)
  const openShortcuts = useDialogs((s) => s.openShortcuts)
  const openManageSlates = useDialogs((s) => s.openManageSlates)
  const ref = useRef<HTMLDivElement>(null)
  const page = useStatusPage().data
  const stale = !!page?.exists && page.stale
  const { write, writing } = useWriteStatusReport()

  const writeReport = async () => {
    setOpen(false)
    await write()
  }

  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        aria-label="Menu"
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={() => setOpen((v) => !v)}
        className="relative inline-flex items-center justify-center h-8 w-8 rounded-md border border-zinc-300 dark:border-zinc-700 text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800"
      >
        <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden>
          <path
            d="M2 4h12M2 8h12M2 12h12"
            stroke="currentColor"
            strokeWidth="1.5"
            strokeLinecap="round"
          />
        </svg>
        {stale && <StaleDot generated={page.generated} className="absolute -top-0.5 -right-0.5" />}
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 mt-2 min-w-[12rem] rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-md p-3 flex flex-col gap-3 z-20"
        >
          <div className="flex items-center justify-between gap-3">
            <span className="text-xs uppercase tracking-wide text-zinc-500">Theme</span>
            <ThemeToggle />
          </div>
          <div className="flex items-center justify-between gap-3 border-t border-zinc-200 dark:border-zinc-800 pt-2">
            <span className="text-xs uppercase tracking-wide text-zinc-500">Text size</span>
            <MarkdownSizeToggle />
          </div>
          <button
            type="button"
            role="menuitem"
            onClick={() => { setOpen(false); openManageSlates() }}
            className="flex items-center gap-2 border-t border-zinc-200 dark:border-zinc-800 pt-2 text-sm text-zinc-700 dark:text-zinc-300 hover:text-blue-600 dark:hover:text-blue-400"
          >
            <Rows3 className="h-4 w-4" aria-hidden />
            Manage slates…
          </button>
          <button
            type="button"
            role="menuitem"
            disabled={writing}
            onClick={() => { void writeReport() }}
            className="flex items-center gap-2 border-t border-zinc-200 dark:border-zinc-800 pt-2 text-sm text-zinc-700 dark:text-zinc-300 hover:text-blue-600 dark:hover:text-blue-400 disabled:opacity-50"
          >
            <FileText className="h-4 w-4" aria-hidden />
            Write status report
            {stale && <StaleDot generated={page.generated} className="ml-auto" />}
          </button>
          {page?.exists && (
            <Link
              to="/status-report"
              role="menuitem"
              onClick={() => setOpen(false)}
              className="flex items-center gap-2 text-sm text-zinc-700 dark:text-zinc-300 hover:text-blue-600 dark:hover:text-blue-400"
            >
              <BookOpenText className="h-4 w-4" aria-hidden />
              View status report
            </Link>
          )}
          <button
            type="button"
            role="menuitem"
            onClick={() => { setOpen(false); openShortcuts() }}
            className="flex items-center gap-2 border-t border-zinc-200 dark:border-zinc-800 pt-2 text-sm text-zinc-700 dark:text-zinc-300 hover:text-blue-600 dark:hover:text-blue-400"
          >
            <Keyboard className="h-4 w-4" aria-hidden />
            Keyboard shortcuts
            <kbd className="ml-auto font-mono text-[11px] bg-zinc-100 dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-700 rounded px-1.5 py-0.5">?</kbd>
          </button>
          <div className="flex items-center justify-between gap-3 border-t border-zinc-200 dark:border-zinc-800 pt-2">
            <span className="text-xs uppercase tracking-wide text-zinc-500">Version</span>
            <VersionIndicator />
          </div>
        </div>
      )}
    </div>
  )
}
