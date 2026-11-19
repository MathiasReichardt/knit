// Regenerates the NuGet and npm sections of THIRD-PARTY-NOTICES.md.
//
//   node tools/gen-notices.mjs                   # rewrite both sections
//   node tools/gen-notices.mjs --check-licenses  # every dependency is permissive (dotnet test)
//   node tools/gen-notices.mjs --check           # the file matches the output (release job)
//
// NuGet: the resolved closure in src/Knit.Cli/packages.lock.json (the shipped
// binary), each package's .nuspec from the NuGet cache — needs a restore first.
// npm: the SPA's production tree from package-lock.json, each package's own
// license file for the copyright line — needs `npm ci` in src/Knit.Web first.
// MIT / ISC / BSD require the copyright notice to travel with a binary
// redistribution, not just the license name.

import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..')
const WEB = join(ROOT, 'src', 'Knit.Web')
const CLI_LOCK = join(ROOT, 'src', 'Knit.Cli', 'packages.lock.json')
const NUGET_CACHE = process.env.NUGET_PACKAGES || join(homedir(), '.nuget', 'packages')
const NOTICES = join(ROOT, 'THIRD-PARTY-NOTICES.md')

const PERMISSIVE = new Set([
  'MIT',
  'ISC',
  'BSD-2-Clause',
  'BSD-3-Clause',
  'Apache-2.0',
  'MPL-2.0',
  '0BSD',
  'Unlicense',
])

// Packages whose metadata can't be taken at face value. Each entry is a
// decision recorded in design/decisions.md, not a guess.
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

// The AOT toolchain: what it links in is the .NET runtime, attributed in the
// paragraph below the NuGet table.
const NUGET_TOOLCHAIN = new Set(['Microsoft.DotNet.ILCompiler', 'Microsoft.NET.ILLink.Tasks'])

// Licenses that impose no attribution condition at all — listed for
// completeness, but nothing is required of us.
const NO_ATTRIBUTION = new Set(['0BSD', 'Unlicense'])

// Matches LICENSE, LICENSE.md, LICENSE-MIT, LICENSE-MIT.txt, COPYING, NOTICE.
// The suffix class excludes '.' so scripts like `license-update.mjs` don't
// pass as license files.
const LICENSE_FILE = /^(licen[cs]e|copying|notice)([-_][\w+-]+)?(\.(md|txt|rst))?$/i

function fail(message) {
  console.error(message)
  process.exit(1)
}

// --- NuGet -------------------------------------------------------------------

function xmlText(xml, tag) {
  const m = xml.match(new RegExp(`<${tag}(\\s[^>]*)?>([^<]*)</${tag}>`))
  if (!m) return null
  return m[2]
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&')
    .trim()
}

function collectNuget() {
  const lock = JSON.parse(readFileSync(CLI_LOCK, 'utf8'))
  // The RID-specific sections only add runtime packs of the AOT toolchain.
  const tfm = Object.keys(lock.dependencies).find((k) => !k.includes('/'))
  const packages = []
  for (const [name, entry] of Object.entries(lock.dependencies[tfm])) {
    if (entry.type === 'Project' || NUGET_TOOLCHAIN.has(name)) continue
    const lower = name.toLowerCase()
    const nuspec = join(NUGET_CACHE, lower, entry.resolved, `${lower}.nuspec`)
    if (!existsSync(nuspec)) fail(`${name} ${entry.resolved} not in the NuGet cache — run: dotnet restore`)
    const xml = readFileSync(nuspec, 'utf8')
    if (xmlText(xml, 'developmentDependency') === 'true') continue

    const override = OVERRIDES[name]
    const expression = /<license\s[^>]*type="expression"/.test(xml) ? xmlText(xml, 'license') : null
    packages.push({
      name,
      version: entry.resolved,
      license: override?.license ?? expression ?? 'UNKNOWN',
      note: override?.note ?? null,
      copyright: xmlText(xml, 'copyright') ?? xmlText(xml, 'authors'),
      project: xmlText(xml, 'projectUrl'),
    })
  }
  return packages.sort((a, b) => a.name.localeCompare(b.name, 'en', { sensitivity: 'base' }))
}

function renderNuget(packages) {
  const cell = (s) => (s ?? '').replace(/\|/g, '\\|')
  const out = ['| Package | Version | License | Copyright | Project |', '|---|---|---|---|---|']
  for (const p of packages) {
    const license = p.note ? `${p.license} *(${p.note})*` : p.license
    out.push(`| ${p.name} | ${p.version} | ${cell(license)} | ${cell(p.copyright)} | ${cell(p.project)} |`)
  }
  out.push('', `Total: ${packages.length} packages.`)
  return out.join('\n')
}

// --- npm ---------------------------------------------------------------------

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

function collectNpm() {
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

function renderNpm(packages) {
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

// --- main --------------------------------------------------------------------

function replaceSection(md, marker, body) {
  const start = `<!-- ${marker}:start -->`
  const end = `<!-- ${marker}:end -->`
  const from = md.indexOf(start)
  const to = md.indexOf(end)
  if (from < 0 || to < 0) fail(`markers ${start} / ${end} not found in THIRD-PARTY-NOTICES.md`)
  return `${md.slice(0, from + start.length)}\n\n${body}\n\n${md.slice(to)}`
}

const nuget = collectNuget()
const npm = collectNpm()

// A dual license (`A OR B`) is not in the set either: it needs an OVERRIDES
// entry that elects one, so the notice names the license that applies.
const rejected = [
  ...nuget.map((p) => ({ ...p, source: 'NuGet' })),
  ...npm.map((p) => ({ ...p, source: 'npm' })),
].filter((p) => !PERMISSIVE.has(p.license))
if (rejected.length)
  fail(
    'unresolved or non-permissive license:\n' +
      rejected.map((p) => `  ${p.source} ${p.name} ${p.version}: ${p.license}`).join('\n') +
      '\nA gap or dual license: add an OVERRIDES entry, with the reasoning in design/decisions.md.' +
      '\nA copyleft or source-available license: the dependency can\'t ship.',
  )

const count = `${nuget.length} NuGet + ${npm.length} npm packages`
if (process.argv.includes('--check-licenses')) {
  console.log(`all permissive (${count})`)
  process.exit(0)
}

const md = readFileSync(NOTICES, 'utf8')
const next = replaceSection(replaceSection(md, 'nuget-list', renderNuget(nuget)), 'npm-list', renderNpm(npm))
if (process.argv.includes('--check')) {
  if (next !== md) fail('THIRD-PARTY-NOTICES.md is stale — run: node tools/gen-notices.mjs')
  console.log(`up to date (${count})`)
} else {
  writeFileSync(NOTICES, next)
  console.log(`wrote ${count} to THIRD-PARTY-NOTICES.md`)
}
