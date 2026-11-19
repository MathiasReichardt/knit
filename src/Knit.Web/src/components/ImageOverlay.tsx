import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { Minimize2 } from 'lucide-react'

/** An image from a doc body, maximized. Esc, the Close button or a click closes it. */
export function ImageOverlay({ src, alt, onClose }: { src: string; alt: string; onClose: () => void }) {
  const closeRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    closeRef.current?.focus()
    // Capture phase: Esc closes the overlay only, not whatever else listens for it.
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.preventDefault()
      e.stopPropagation()
      onClose()
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [onClose])

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-label={alt || 'Image'}
      className="fixed inset-0 z-40 flex items-center justify-center bg-white dark:bg-zinc-950 p-4 cursor-zoom-out"
      onClick={onClose}
      onDoubleClick={(e) => e.stopPropagation()}
    >
      <img src={src} alt={alt} className="max-h-full max-w-full object-contain" />
      <button
        ref={closeRef}
        type="button"
        className={
          'absolute top-5 right-5 inline-flex h-6 items-center gap-1 rounded border border-zinc-200 dark:border-zinc-700 ' +
          'bg-white/90 dark:bg-zinc-900/90 px-1.5 text-xs text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800'
        }
        title="Close (Esc)"
        onClick={onClose}
      >
        <Minimize2 className="h-3.5 w-3.5" aria-hidden />
        Close
      </button>
    </div>,
    document.body,
  )
}
