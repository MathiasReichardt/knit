// A Milestone link row stores its target as a relative markdown path to the
// sub-doc: `.../projects/<project>/milestones/<slug>.md`. The delete endpoint
// matches by the qualified ref `<project>/<slug>` (same shape the CLI unlink
// takes), so extract that from the stored path. Returns null when the path
// isn't milestone-shaped. Mirrors Core's LinkPath.MilestoneFromTarget.
export function milestoneRefFromPath(targetPath: string): string | null {
  const m = targetPath
    .replace(/\\/g, '/')
    .match(/projects\/([^/]+)\/milestones\/(.+)\.md$/)
  if (!m) return null
  return `${m[1]}/${m[2]}`
}

// The relative markdown path a project's roadmap.md (or a sibling doc) uses to
// text-link a milestone. Roadmap lives at `.knit/projects/<project>/roadmap.md`
// and the milestone at `.knit/projects/<project>/milestones/<slug>.md`, so from
// the roadmap the link is `milestones/<slug>.md`. Copy-paste target for roadmap prose.
export function milestoneDocLink(slug: string): string {
  return `milestones/${slug}.md`
}
