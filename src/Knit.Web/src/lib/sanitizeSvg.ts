// The sanitizer for a diagram SVG file (mermaid/svgFile.ts draws it); kept out
// of src/mermaid/, which imports no sanitizer.

import DOMPurify from 'dompurify'

// A separate instance: its hook must not touch the markdown sanitizer's.
const purify = DOMPurify(window)
purify.addHook('uponSanitizeAttribute', (_node, data) => {
  // Only in-file references: an external href fetches, or navigates, from the SPA's origin.
  if ((data.attrName === 'href' || data.attrName === 'xlink:href') && !data.attrValue.trim().startsWith('#')) {
    data.keepAttr = false
  }
})

/**
 * The file as an inlinable `<svg>`: no script, `foreignObject`, `<style>` (it
 * would restyle the whole page), event handler or external href. Null when the
 * text holds no SVG.
 */
export function sanitizeSvg(text: string): SVGSVGElement | null {
  const clean = purify.sanitize(text, {
    USE_PROFILES: { svg: true, svgFilters: true },
    FORBID_TAGS: ['style', 'foreignObject', 'script'],
    RETURN_DOM_FRAGMENT: true,
  })
  const svg = clean.querySelector('svg')
  return svg instanceof SVGSVGElement ? svg : null
}
