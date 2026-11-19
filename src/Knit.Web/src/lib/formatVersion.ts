export interface VersionInfo {
  version: string
  formatVersion?: number
  supportedFormatVersion?: number
}

/**
 * Warn when the repo on disk was written by a newer knit than this server
 * understands. Read-only views may still render, so this is a warning — the
 * CLI is the one that hard-refuses. Older/equal repos are always fine.
 */
export function formatVersionWarning(info: VersionInfo | undefined): string | null {
  if (!info) return null
  const repo = info.formatVersion
  const supported = info.supportedFormatVersion
  if (typeof repo !== 'number' || typeof supported !== 'number') return null
  if (repo <= supported) return null
  return `This repo uses knit format ${repo}; this server understands ${supported}. Upgrade knit — data may be shown incompletely.`
}
