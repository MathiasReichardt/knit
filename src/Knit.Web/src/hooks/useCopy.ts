import { useState } from 'react'

// Copy to the clipboard and flag `copied` briefly, for a check-mark confirmation.
export function useCopy(value: string) {
  const [copied, setCopied] = useState(false)
  const copy = (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    if (!value) return
    navigator.clipboard.writeText(value).then(
      () => {
        setCopied(true)
        window.setTimeout(() => setCopied(false), 1200)
      },
      () => {},
    )
  }
  return { copied, copy }
}
