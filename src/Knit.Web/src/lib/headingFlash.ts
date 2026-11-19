/**
 * Scroll the element with `id` into view within `root` and flash it so the
 * jump target is obvious. Shared by the Designs/KB outline panes (heading
 * anchors) and the items details pane (matched search region) so the
 * scroll-and-pulse behaviour can't drift. No-op when the id isn't found.
 *
 * The flash is applied on the next animation frame, not synchronously. Clicking
 * an outline row also flips the active-heading state, and React commits that
 * re-render between this call and the next paint — applying the `knit-flash`
 * class synchronously gets clobbered by that commit, so the pulse only showed
 * on the second click. Deferring past the commit — and re-querying the node —
 * fixes the first click.
 *
 * Resilience to re-render: the flashed node can live inside body
 * markdown rendered via `dangerouslySetInnerHTML` (a heading). A background
 * refetch (SSE/focus invalidation) re-renders that body once, ~0.3s in, which
 * re-sets the container's innerHTML and *detaches* the flashing node — the
 * animation is cancelled and the pulse vanished in well under its 3s. So we
 * watch `root` for the target id reappearing and re-apply the class to the
 * replacement node, resuming the animation via a negative `animation-delay` so
 * the total duration stays put. The flash is gated behind
 * `prefers-reduced-motion` in index.css.
 */
// Keep in sync with the `knit-flash` animation duration in index.css.
export const FLASH_MS = 1800

export function scrollAndFlash(root: HTMLElement, id: string): void {
  const selector = `#${CSS.escape(id)}`
  const el = root.querySelector<HTMLElement>(selector)
  if (!el) return

  el.scrollIntoView({ behavior: 'smooth', block: 'start' })
  flashIn(root, selector)
}

/** Flash the element matching `selector` within `root`, without scrolling. */
export function flashIn(root: HTMLElement, selector: string): void {
  requestAnimationFrame(() => {
    const node = root.querySelector<HTMLElement>(selector)
    if (!node) return
    flash(root, selector, node)
  })
}

function flash(root: HTMLElement, selector: string, first: HTMLElement): void {
  const start = performance.now()

  const apply = (target: HTMLElement, elapsedMs: number) => {
    target.classList.remove('knit-flash')
    void target.offsetWidth // force reflow so re-adding the class restarts the animation
    // Resume mid-animation on a replacement node so a re-render mid-flash
    // doesn't reset the perceived duration to zero.
    target.style.animationDelay = elapsedMs > 0 ? `-${Math.round(elapsedMs)}ms` : ''
    target.classList.add('knit-flash')
  }

  const stop = () => {
    obs.disconnect()
    const cur = root.querySelector<HTMLElement>(selector)
    if (cur) {
      cur.classList.remove('knit-flash')
      cur.style.animationDelay = ''
    }
  }

  // Re-apply to the replacement node when a re-render swaps it out mid-flash.
  const obs = new MutationObserver(() => {
    const elapsed = performance.now() - start
    if (elapsed >= FLASH_MS) { stop(); return }
    const fresh = root.querySelector<HTMLElement>(selector)
    if (fresh && !fresh.classList.contains('knit-flash')) apply(fresh, elapsed)
  })

  apply(first, 0)
  obs.observe(root, { childList: true, subtree: true })
  // Backstop: clear once the window has elapsed even if no further mutations
  // fire (the observer only ticks on DOM churn).
  window.setTimeout(stop, FLASH_MS + 100)
}
