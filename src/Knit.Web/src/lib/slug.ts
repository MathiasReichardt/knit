// Client-side mirror of Knit.Core.Ids.Slug.From — used only to preview the
// slug the backend will derive from a title in the New-KB dialog. The server
// remains the source of truth; this just has to match closely enough that the
// preview is honest. Keep in sync with src/Knit.Core/Ids/Slug.cs.

const MAX_LEN = 40

// Hand-rolled ASCII fold for common Latin diacritics, mirroring Slug.cs.
const FOLDS: Record<string, string> = {
  ä: 'a', Ä: 'a', ö: 'o', Ö: 'o', ü: 'u', Ü: 'u', ß: 'ss',
  á: 'a', à: 'a', â: 'a', ã: 'a', å: 'a', Á: 'a', À: 'a', Â: 'a', Ã: 'a', Å: 'a',
  é: 'e', è: 'e', ê: 'e', ë: 'e', É: 'e', È: 'e', Ê: 'e', Ë: 'e',
  í: 'i', ì: 'i', î: 'i', ï: 'i', Í: 'i', Ì: 'i', Î: 'i', Ï: 'i',
  ó: 'o', ò: 'o', ô: 'o', õ: 'o', ø: 'o', Ó: 'o', Ò: 'o', Ô: 'o', Õ: 'o', Ø: 'o',
  ú: 'u', ù: 'u', û: 'u', Ú: 'u', Ù: 'u', Û: 'u',
  ñ: 'n', Ñ: 'n', ç: 'c', Ç: 'c', æ: 'ae', Æ: 'ae', œ: 'oe', Œ: 'oe', ý: 'y', ÿ: 'y',
}

// The full slugified form before the MAX_LEN cap is applied. Returns '' when
// the title yields no slug characters at all.
function build(title: string): string {
  let out = ''
  let lastDash = false
  const append = (ch: string) => {
    const c = ch >= 'A' && ch <= 'Z' ? ch.toLowerCase() : ch
    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) {
      out += c
      lastDash = false
      return
    }
    if (!lastDash && out.length > 0) {
      out += '-'
      lastDash = true
    }
  }

  for (const ch of title) {
    const folded = FOLDS[ch]
    if (folded !== undefined) {
      for (const fc of folded) append(fc)
      continue
    }
    append(ch)
  }

  return out.replace(/^-+|-+$/g, '')
}

export function slugify(title: string): string {
  if (!title.trim()) return 'untitled'
  const full = build(title)
  if (full.length === 0) return 'untitled'
  if (full.length > MAX_LEN) {
    const cut = full.slice(0, MAX_LEN).replace(/-+$/, '')
    return cut.length === 0 ? 'untitled' : cut
  }
  return full
}

// Whether slugify(title) had to drop characters to fit MAX_LEN — used to tell
// the user their derived slug was shortened.
export function isSlugTruncated(title: string): boolean {
  if (!title.trim()) return false
  return build(title).length > MAX_LEN
}
