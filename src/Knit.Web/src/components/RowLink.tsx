import type { AnchorHTMLAttributes, MouseEvent } from 'react'
import { useRouter } from '@tanstack/react-router'

interface Props extends Omit<AnchorHTMLAttributes<HTMLAnchorElement>, 'href' | 'onClick'> {
  to: string
  search?: Record<string, unknown>
  // A plain left click (or Enter) — the row's in-app selection.
  onSelect?: (e: MouseEvent<HTMLAnchorElement>) => void
  // Rows whose Ctrl/Shift+click means multi-select keep those clicks;
  // elsewhere they fall through to the browser (open in a new tab).
  claimModifiers?: boolean
  // Merge `search` into the current URL's params (the items view keeps its
  // filters and tree/list choice in the URL) instead of replacing them.
  keepSearch?: boolean
  disabled?: boolean
}

// A selectable row label rendered as a real anchor, so middle-click,
// Ctrl+click and the browser context menu open the target in a new tab.
// Styled like the button it replaces: no pointer cursor, no native link drag.
export function RowLink({
  to,
  search,
  onSelect,
  claimModifiers = false,
  keepSearch = false,
  disabled = false,
  className = '',
  children,
  ...rest
}: Props) {
  const router = useRouter()
  const build = () =>
    router.buildLocation({
      to,
      search: keepSearch ? (prev: Record<string, unknown>) => ({ ...prev, ...search }) : search,
    } as never).href
  const href = disabled ? undefined : build()
  // A row isn't re-rendered when only the filters change, so its href can lag
  // the URL; refresh it right before the browser follows it.
  const refresh = (e: MouseEvent<HTMLAnchorElement>) => {
    if (keepSearch && !disabled) e.currentTarget.href = build()
  }
  return (
    <a
      {...rest}
      href={href}
      draggable={false}
      aria-disabled={disabled || undefined}
      onMouseDown={refresh}
      onContextMenu={(e) => {
        refresh(e)
        rest.onContextMenu?.(e)
      }}
      className={(className.includes('cursor-') ? '' : 'cursor-default ') + className}
      onClick={(e) => {
        if (e.button !== 0) return
        if (!claimModifiers && (e.ctrlKey || e.metaKey || e.shiftKey || e.altKey)) {
          e.stopPropagation()
          return
        }
        e.preventDefault()
        if (!disabled) onSelect?.(e)
      }}
    >
      {children}
    </a>
  )
}
