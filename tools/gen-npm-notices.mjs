// Regenerates the npm section of THIRD-PARTY-NOTICES.md from the SPA's
// lockfile + the license files in node_modules.
//
//   node tools/gen-npm-notices.mjs          # rewrite the section
//   node tools/gen-npm-notices.mjs --check  # fail if it would change (CI)
//
// MIT / ISC / BSD all require the *copyright notice* to travel with a binary
// redistribution, not just the license text — so this reads each package's own
// license file rather than trusting the lockfile's `license` field alone.
// Needs `npm ci` in src/Knit.Web first; the license files only exist on disk.

import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..')
const WEB = join(ROOT, 'src', 'Knit.Web')
const NOTICES = join(ROOT, 'THIRD-PARTY-NOTICES.md')
const START = '<!-- npm-list:start -->'
const END = '<!-- npm-list:end -->'

// Packages whose lockfile metadata can't be taken at face value. Each entry is
// a decision recorded in design/decisions.md, not a guess.
const OVERRIDES = {
  dompurify: {
    license: 'Apache-2.0',
    note: 'dual-licensed `MPL-2.0 OR Apache-2.0`; Apache-2.0 elected',
  },
  khroma: {
    license: 'MIT',
    note: 'no `license` field in the lockfile; MIT per the license file it ships',
  },
}

// Licenses that impose no attribution condition at all — listed for
// completeness, but nothing is required of us.
const NO_ATTRIBUTION = new Set(['0BSD', 'Unlicense'])

// Matches LICENSE, LICENSE.md, LICENSE-MIT, LICENSE-MIT.txt, COPYING, NOTICE.
// The suffix class excludes '.' so scripts like `license-update.mjs` don't
// pass as license files.
const LICENSE_FILE = /^(licen[cs]e|copying|notice)([-_][\w+-]+)?(\.(md|txt|rst))?$/i

function licenseTexts(dir) {
  if (!existsSync(dir)) return []
  try {
    return readdirSync(dir)
      .filter((f) => LICENSE_FILE.test(f))
      .map((f) => {
        try {
          return readFileSync(join(dir, f), 'utf8')
        } catch {
          return null
        }
      })
      .filter(Boolean)
  } catch {
    return []
  }
}

function copyrightIn(text) {
  return text
    .split(/\r?\n/)
    .map((l) => l.trim())
    .find((l) => /copyright/i.test(l) && /\d{4}|\(c\)|©/i.test(l))
}

function copyrightOf(dir, pkg) {
  // A package can ship several license files (dompurify: LICENSE + LICENSE-MPL);
  // take the first that actually carries a notice line.
  const line = licenseTexts(dir).map(copyrightIn).find(Boolean)
  if (line) return line.replace(/^[*#\-\s]+/, '').replace(/\s+/g, ' ')

  // No license file, or one without a notice line (common for ISC/0BSD).
  // The author field is the next-best attribution the package offers.
  const author = pkg?.author
  const name = typeof author === 'string' ? author : author?.name
  return name ? `${name}` : null
}

function collect() {
  const lock = JSON.parse(readFileSync(join(WEB, 'package-lock.json'), 'utf8'))
  const seen = new Map()
  for (const [path, entry] of Object.entries(lock.packages)) {
    if (!path.startsWith('node_modules/') || entry.dev) continue
    const name = path.replace(/^node_modules\//, '').replace(/.*node_modules\//, '')
    const key = `${name}@${entry.version}`
    if (seen.has(key)) continue

    const dir = join(WEB, path)
    let pkg = null
    try {
      pkg = JSON.parse(readFileSync(join(dir, 'package.json'), 'utf8'))
    } catch {
      // Package not installed — fall back to lockfile metadata only.
    }

    const override = OVERRIDES[name]
    const raw = entry.license ?? pkg?.license
    seen.set(key, {
      name,
      version: entry.version,
      license: override?.license ?? (Array.isArray(raw) ? raw.join(' OR ') : raw) ?? 'UNKNOWN',
      note: override?.note ?? null,
      copyright: copyrightOf(dir, pkg),
    })
  }
  return [...seen.values()].sort((a, b) => a.name.localeCompare(b.name))
}

function render(packages) {
  const groups = new Map()
  for (const p of packages) {
    if (!groups.has(p.license)) groups.set(p.license, [])
    groups.get(p.license).push(p)
  }
  const order = ['MIT', 'ISC', 'BSD-3-Clause', 'Apache-2.0', '0BSD', 'Unlicense']
  const keys = [
    ...order.filter((k) => groups.has(k)),
    ...[...groups.keys()].filter((k) => !order.includes(k)).sort(),
  ]

  const out = []
  for (const key of keys) {
    const group = groups.get(key)
    out.push(`### ${key} (${group.length})`, '')
    if (NO_ATTRIBUTION.has(key))
      out.push(`_${key} imposes no attribution condition; listed for completeness._`, '')
    for (const p of group) {
      const parts = [`- **${p.name}** ${p.version}`]
      if (p.copyright) parts.push(` — ${p.copyright}`)
      if (p.note) parts.push(` *(${p.note})*`)
      out.push(parts.join(''))
    }
    out.push('')
  }
  const missing = packages.filter((p) => !p.copyright && !NO_ATTRIBUTION.has(p.license))
  if (missing.length)
    out.push(
      `_${missing.length} package(s) above ship no copyright notice and name no author; ` +
        'there is nothing further to reproduce for them._',
      '',
    )
  out.push(`Total: ${packages.length} packages.`)
  return out.join('\n')
}

const packages = collect()
const unknown = packages.filter((p) => p.license === 'UNKNOWN')
if (unknown.length) {
  console.error(
    `unresolved license for: ${unknown.map((p) => p.name).join(', ')}\n` +
      'Resolve each by hand and add it to OVERRIDES, with the reasoning in design/decisions.md.',
  )
  process.exit(1)
}

const md = readFileSync(NOTICES, 'utf8')
const from = md.indexOf(START)
const to = md.indexOf(END)
if (from < 0 || to < 0) {
  console.error(`markers ${START} / ${END} not found in THIRD-PARTY-NOTICES.md`)
  process.exit(1)
}

const next = `${md.slice(0, from + START.length)}\n\n${render(packages)}\n\n${md.slice(to)}`
if (process.argv.includes('--check')) {
  if (next !== md) {
    console.error('THIRD-PARTY-NOTICES.md is stale — run: node tools/gen-npm-notices.mjs')
    process.exit(1)
  }
  console.log(`up to date (${packages.length} packages)`)
} else {
  writeFileSync(NOTICES, next)
  console.log(`wrote ${packages.length} packages to THIRD-PARTY-NOTICES.md`)
}
