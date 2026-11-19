import { describe, expect, it } from 'vitest'
import { priorityMarkerFill, priorityRowTint } from './priority'

/**
 * A measured floor for the priority palette, in both themes.
 *
 * `critical` and `important` had drifted to near-identical dark tints because
 * "looks different enough" was the only check. This test replaces the eye with
 * a number: it composites each class exactly as the browser would (alpha over
 * the page ground, in sRGB) and measures the distance in OKLab — the space
 * Tailwind's colors are authored in, where Euclidean distance tracks perceived
 * difference. WCAG contrast ratio is the wrong tool here: two tints that
 * differ only in hue score ~1.0 no matter how far apart they read.
 *
 * The *classes* come from the components, so retuning a tint re-measures it.
 */

// Distance at which two flat colors read as different at a glance. ~0.02 is a
// just-noticeable difference for two patches side by side; a tint compared
// across rows, never edge to edge, needs several times that. The light theme
// (which nobody complained about) sits at 0.066 — this floor is that, minus
// rounding room, so "as good as light mode" is the bar.
const FLOOR = 0.06

// Tailwind v4 palette, `oklch(L C h)` as published in `tailwindcss/theme.css`
// (the CSS can't be imported here — vitest stubs CSS imports). Only the
// colors the tints actually use; an unlisted one throws rather than silently
// skipping the measurement.
const PALETTE: Record<string, [number, number, number]> = {
  'amber-100': [0.962, 0.059, 95.617],
  'amber-200': [0.924, 0.12, 95.746],
  'amber-900': [0.414, 0.112, 45.904],
  'red-100': [0.936, 0.032, 17.717],
  'red-200': [0.885, 0.062, 18.334],
  'red-800': [0.444, 0.177, 26.899],
  'blue-900': [0.379, 0.146, 265.522],
  'zinc-900': [0.21, 0.006, 285.885],
  'zinc-950': [0.141, 0.005, 285.823],
}

type Rgb = [number, number, number]

const encode = (x: number) => (x <= 0.0031308 ? 12.92 * x : 1.055 * x ** (1 / 2.4) - 0.055)
const decode = (x: number) => (x <= 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4)
const clamp = (x: number) => Math.min(1, Math.max(0, x))

function oklchToRgb([L, C, h]: [number, number, number]): Rgb {
  const a = C * Math.cos((h * Math.PI) / 180)
  const b = C * Math.sin((h * Math.PI) / 180)
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3
  return [
    clamp(encode(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s)),
    clamp(encode(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s)),
    clamp(encode(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s)),
  ]
}

function rgbToOklab(rgb: Rgb): Rgb {
  const [r, g, b] = rgb.map(decode)
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)
  return [
    0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s,
  ]
}

const color = (name: string): Rgb => {
  const c = PALETTE[name]
  if (!c) throw new Error(`add ${name} to PALETTE (from tailwindcss/theme.css) to measure it`)
  return oklchToRgb(c)
}

/** Composite `fg` at `alpha` over `bg`, the way a translucent background is
 *  painted over the page ground. */
const over = (fg: Rgb, alpha: number, bg: Rgb): Rgb =>
  fg.map((c, i) => alpha * c + (1 - alpha) * bg[i]) as Rgb

const distance = (a: Rgb, b: Rgb) => {
  const [x, y, z] = rgbToOklab(a)
  const [p, q, r] = rgbToOklab(b)
  return Math.hypot(x - p, y - q, z - r)
}

const WHITE: Rgb = [1, 1, 1]

/** Resolve one utility class (`bg-red-800/45`, `fill-amber-900`) to the color
 *  it paints over `ground`. */
function paint(cls: string, ground: Rgb): Rgb {
  const m = /^(?:bg|fill)-([a-z]+-\d+)(?:\/(\d+))?$/.exec(cls)
  if (!m) throw new Error(`not a palette class: ${cls}`)
  return over(color(m[1]), m[2] ? Number(m[2]) / 100 : 1, ground)
}

/** Pick the class carrying exactly `prefix` out of a class list (so
 *  `dark:bg-…` is never confused with `dark:hover:bg-…`). */
function variant(classes: string, prefix: '' | 'hover:' | 'dark:' | 'dark:hover:'): string {
  const hit = classes
    .split(/\s+/)
    .filter((c) => c.startsWith(prefix))
    .map((c) => c.slice(prefix.length))
    .filter((c) => !c.includes(':'))
  if (hit.length !== 1) throw new Error(`expected one '${prefix}' class in: ${classes}`)
  return hit[0]
}

const rowTint = (p: 'important' | 'critical') => {
  const cls = priorityRowTint(p)
  if (!cls) throw new Error(`no tint for ${p}`)
  return cls
}

describe('priority palette separation (measured, OKLab)', () => {
  it('separates critical from important in both themes', () => {
    const cases = [
      ['light', '' as const, WHITE],
      ['light hover', 'hover:' as const, WHITE],
      ['dark', 'dark:' as const, color('zinc-950')],
      ['dark hover', 'dark:hover:' as const, color('zinc-900')],
    ] as const
    for (const [label, prefix, ground] of cases) {
      const d = distance(
        paint(variant(rowTint('important'), prefix), ground),
        paint(variant(rowTint('critical'), prefix), ground),
      )
      expect(d, `${label}: critical vs important = ${d.toFixed(4)}`).toBeGreaterThanOrEqual(FLOOR)
    }
  })

  it('keeps every tint visible against the untinted row it sits among', () => {
    // A tint that clears the floor against its sibling but not against a plain
    // row has only moved the problem.
    for (const p of ['important', 'critical'] as const) {
      for (const [prefix, ground] of [
        ['dark:', color('zinc-950')],
        ['dark:hover:', color('zinc-900')],
      ] as const) {
        const d = distance(paint(variant(rowTint(p), prefix), ground), ground)
        expect(d, `${prefix}${p} vs plain row = ${d.toFixed(4)}`).toBeGreaterThanOrEqual(FLOOR)
      }
    }
  })

  it('separates both tints from the selection blue that overrides them', () => {
    // Selection wins over the tint on the same row, so the two must not read
    // as the same state when scanning a mixed list.
    const ground = color('zinc-950')
    const selection = paint('bg-blue-900/40', ground)
    for (const p of ['important', 'critical'] as const) {
      const d = distance(paint(variant(rowTint(p), 'dark:'), ground), selection)
      expect(d, `dark ${p} vs selection = ${d.toFixed(4)}`).toBeGreaterThanOrEqual(FLOOR)
    }
  })

  it('separates the timeline marker fills, which are read at glyph size', () => {
    const important = priorityMarkerFill('important') ?? ''
    const critical = priorityMarkerFill('critical') ?? ''
    for (const [label, prefix, ground] of [
      ['light', '' as const, WHITE],
      ['dark', 'dark:' as const, color('zinc-950')],
    ] as const) {
      const d = distance(paint(variant(important, prefix), ground), paint(variant(critical, prefix), ground))
      expect(d, `${label}: marker fills = ${d.toFixed(4)}`).toBeGreaterThanOrEqual(FLOOR)
    }
  })
})
