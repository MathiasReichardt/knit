import { useCallback, useEffect, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import { api } from '../api/client'

/**
 * Persisted expand state for every navigation tree.
 *
 * Two normalizations make one store fit three trees:
 *
 * - **Every tree tracks `expanded`, never `collapsed`.** `TreeView` and
 *   `ProjectsTree` used to track the collapsed set (default expanded);
 *   persisting each tree's own sense would have silently flipped defaults, and
 *   a newly added item would have arrived expanded while everything around it
 *   was collapsed.
 * - **An empty store renders exactly what *collapse all* renders** — level 0
 *   only, in every tree. One rule instead of a per-tree exception; the
 *   reveal-ancestors paths (jump-to-ref, search, deep link) still open the
 *   path to a target.
 */
export type TreeName = 'items' | 'kb' | 'projects'

// Node ids are per-tree (item ids here, relPaths there), so each tree gets its
// own namespace and identical keys in two trees stay apart.
type RepoState = Partial<Record<TreeName, string[]>>

interface TreeOpenState {
  // Set from `/api/root` once it resolves; null means "not yet keyed", which is
  // why trees gate their rows on `ready` rather than render a collapse-all
  // flash and snap out of it.
  repo: string | null
  byRepo: Record<string, RepoState>
  setRepo: (repo: string) => void
  toggle: (tree: TreeName, id: string) => void
  replace: (tree: TreeName, ids: string[]) => void
  expand: (tree: TreeName, ids: string[]) => void
  prune: (tree: TreeName, alive: ReadonlySet<string>) => void
}

/**
 * Storage key for a repo. The **effective knit root** is the identity: it is
 * what `/api/root` reports, and the root redirect points a worktree at
 * the main repo's `.knit/` — so worktrees sharing knit state share this key,
 * while a worktree keeping its own `.knit/` keeps its own state. Lower-cased
 * and slash-normalized because Windows paths are case-insensitive and one repo
 * must not split into two keys.
 */
export const repoKey = (rootPath: string) =>
  rootPath.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase()

const update = (s: TreeOpenState, tree: TreeName, next: (prev: string[]) => string[]) => {
  if (!s.repo) return s
  const repoState = s.byRepo[s.repo] ?? {}
  const prev = repoState[tree] ?? []
  const value = next(prev)
  if (value === prev) return s
  return { byRepo: { ...s.byRepo, [s.repo]: { ...repoState, [tree]: value } } }
}

export const useTreeOpenStore = create<TreeOpenState>()(
  persist(
    (set) => ({
      repo: null,
      byRepo: {},
      setRepo: (repo) => set((s) => (s.repo === repo ? s : { repo })),
      toggle: (tree, id) =>
        set((s) =>
          update(s, tree, (prev) =>
            prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id],
          ),
        ),
      replace: (tree, ids) => set((s) => update(s, tree, () => [...new Set(ids)])),
      expand: (tree, ids) =>
        set((s) =>
          update(s, tree, (prev) =>
            ids.every((i) => prev.includes(i)) ? prev : [...new Set([...prev, ...ids])],
          ),
        ),
      // Unknown is not deleted: callers must pass a complete, unfiltered node
      // set, or merely-hidden nodes would lose their expand state.
      prune: (tree, alive) =>
        set((s) =>
          update(s, tree, (prev) => {
            const kept = prev.filter((id) => alive.has(id))
            return kept.length === prev.length ? prev : kept
          }),
        ),
    }),
    {
      name: 'knit.treeOpen',
      storage: createJSONStorage(() => localStorage),
      partialize: (s) => ({ byRepo: s.byRepo }),
    },
  ),
)

/**
 * Bind a tree to the persisted store. `ready` is false until `/api/root`
 * resolves the repo key — trees hold their rows until then, so a saved
 * expansion never renders as collapse-all first.
 *
 * `seed` is the level-0 set for a tree whose root is itself a node (the KB's
 * `''` bucket root). It applies only while this repo has never persisted the
 * tree, so a deliberate collapse-all is not re-seeded. Pass a module-level
 * constant — it is a render dependency.
 */
export function useTreeOpen(tree: TreeName, seed: readonly string[] = []) {
  const rootQ = useQuery({ queryKey: ['root'], queryFn: api.root, staleTime: Infinity })
  const key = rootQ.data ? repoKey(rootQ.data.rootPath) : null

  const setRepo = useTreeOpenStore((s) => s.setRepo)
  useEffect(() => {
    if (key) setRepo(key)
  }, [key, setRepo])

  const repo = useTreeOpenStore((s) => s.repo)
  const stored = useTreeOpenStore((s) => (s.repo ? s.byRepo[s.repo]?.[tree] : undefined))
  const expanded = useMemo(() => new Set(stored ?? seed), [stored, seed])

  const toggle = useCallback((id: string) => useTreeOpenStore.getState().toggle(tree, id), [tree])
  const replace = useCallback((ids: string[]) => useTreeOpenStore.getState().replace(tree, ids), [tree])
  const expand = useCallback((ids: string[]) => useTreeOpenStore.getState().expand(tree, ids), [tree])
  const prune = useCallback(
    (alive: ReadonlySet<string>) => useTreeOpenStore.getState().prune(tree, alive),
    [tree],
  )

  // The store has to have taken the key too, or a toggle in the same tick would
  // be dropped by `update`'s null-repo guard.
  return { ready: key !== null && repo === key, expanded, toggle, replace, expand, prune }
}
