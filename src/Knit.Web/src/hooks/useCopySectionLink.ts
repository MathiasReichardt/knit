import { useToasts } from '../stores/toasts'

/** Copies `<docRef>#<anchor>`, the same link the heading's own icon copies. */
export function useCopySectionLink(docRef: string) {
  const toast = useToasts((s) => s.push)
  return (id: string) =>
    navigator.clipboard.writeText(`${docRef}#${id}`).then(
      () => toast('success', 'Section link copied'),
      () => toast('error', 'Could not copy the section link'),
    )
}
