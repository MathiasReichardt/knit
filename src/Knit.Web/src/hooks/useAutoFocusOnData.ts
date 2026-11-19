import { useEffect, useRef } from 'react'
import { isTyping } from '../lib/arrowNav'

/**
 * Focus a container once, the first time its data arrives, so arrow-key
 * navigation works without clicking into it first. Keyed on the data (not
 * mount) because the focusable node only renders after the loading/empty
 * placeholder is replaced — a mount-only effect would fire while `ref` is
 * still null. Skips when the user is already typing (e.g. landed via the
 * search box) so it never yanks focus away.
 */
export function useAutoFocusOnData(
  ref: React.RefObject<HTMLElement | null>,
  data: unknown,
) {
  const didFocus = useRef(false)
  useEffect(() => {
    if (didFocus.current || !ref.current) return
    didFocus.current = true
    if (!isTyping(document.activeElement)) ref.current.focus({ preventScroll: true })
  }, [ref, data])
}
