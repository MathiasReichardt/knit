import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

/**
 * Rendering size of markdown bodies. Discrete steps on the
 * Tailwind Typography ladder rather than a free scale, so every step is a
 * coherent type scale instead of an arbitrary zoom. `sm` is the historical
 * default — nobody's view changes until they opt in.
 */
export type MarkdownSize = 'sm' | 'base' | 'lg'

export const MARKDOWN_SIZES: readonly MarkdownSize[] = ['sm', 'base', 'lg']

export const DEFAULT_MARKDOWN_SIZE: MarkdownSize = 'sm'

const PROSE_CLASS: Record<MarkdownSize, string> = {
  sm: 'prose-sm',
  base: 'prose-base',
  lg: 'prose-lg',
}

// `not-prose` blocks (code, callouts, mermaid status lines) sit *outside* the
// typography ladder and carry their own size, so they have to be scaled
// alongside it or a bumped step leaves them stuck at the old size.
const NOT_PROSE_CLASS: Record<MarkdownSize, string> = {
  sm: 'text-xs',
  base: 'text-sm',
  lg: 'text-base',
}

export const proseSizeClass = (size: MarkdownSize): string => PROSE_CLASS[size]

export const notProseSizeClass = (size: MarkdownSize): string => NOT_PROSE_CLASS[size]

export const MARKDOWN_SIZE_LABEL: Record<MarkdownSize, string> = {
  sm: 'Small',
  base: 'Medium',
  lg: 'Large',
}

interface MarkdownSizeState {
  size: MarkdownSize
  setSize: (size: MarkdownSize) => void
}

export const useMarkdownSize = create<MarkdownSizeState>()(
  persist(
    (set) => ({
      size: DEFAULT_MARKDOWN_SIZE,
      setSize: (size) => set({ size }),
    }),
    { name: 'knit.markdownSize', storage: createJSONStorage(() => localStorage) },
  ),
)
