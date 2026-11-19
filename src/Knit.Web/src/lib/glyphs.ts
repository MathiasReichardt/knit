import type { ComponentType, SVGProps } from 'react'
import {
  BookOpen,
  Boxes,
  Bug,
  Circle,
  CircleCheck,
  CircleDot,
  CircleSlash,
  Diamond,
  FolderKanban,
  Glasses,
  Layers,
  Lightbulb,
  Link2,
  ListTodo,
  Pencil,
  Scale,
  ScrollText,
  Sparkles,
} from 'lucide-react'
import type { ItemHealth, ItemState, ItemType } from '../api/types'
import type { KbBucket } from '../api/kb'

export type IconCmp = ComponentType<SVGProps<SVGSVGElement>>

// task uses Diamond (not a Square): a square sitting next to the circular
// state glyph read as a checkbox ("click to complete"). A
// diamond is neither a box nor a circle, so type ≠ status at a glance.
export const TYPE_ICON: Record<ItemType, IconCmp> = {
  project: FolderKanban,
  epic: Layers,
  feature: Sparkles,
  bug: Bug,
  task: Diamond,
  todo: ListTodo,
}

export const TYPE_COLOR: Record<ItemType, string> = {
  // cyan for project — the top altitude, matching the CLI's cyan ◆.
  project: 'text-cyan-600 dark:text-cyan-400',
  epic: 'text-fuchsia-600 dark:text-fuchsia-400',
  feature: 'text-blue-600 dark:text-blue-400',
  bug: 'text-red-600 dark:text-red-400',
  // slate (not emerald): task is the lowest-altitude, most common type — a
  // neutral color lets it recede so epic/feature/bug carry the signal, and it
  // doesn't share `done`'s green (which would read as a checkbox).
  task: 'text-slate-500 dark:text-slate-400',
  // todo recedes like task but stays distinct (the PM-view leaf).
  todo: 'text-zinc-500 dark:text-zinc-400',
}

// draft shares the pencil with the edit affordance (a deliberate call): the
// state reads as "still being written". review leaves the circle set the same
// way: glasses, "being read over" — the CLI's ⚯ is the same picture.
export const STATE_ICON: Record<ItemState, IconCmp> = {
  draft: Pencil,
  todo: Circle,
  'in-progress': CircleDot,
  review: Glasses,
  done: CircleCheck,
  dismissed: CircleSlash,
}

export const STATE_COLOR: Record<ItemState, string> = {
  draft: 'text-zinc-400 dark:text-zinc-500',
  todo: 'text-zinc-400 dark:text-zinc-500',
  'in-progress': 'text-yellow-600 dark:text-yellow-400',
  review: 'text-violet-600 dark:text-violet-400',
  done: 'text-emerald-600 dark:text-emerald-400',
  dismissed: 'text-zinc-500 dark:text-zinc-500',
}

export const STATE_LABEL: Record<ItemState, string> = {
  draft: 'Draft',
  todo: 'Todo',
  'in-progress': 'In progress',
  review: 'Review',
  done: 'Done',
  dismissed: 'Dismissed',
}

export const TYPE_LABEL: Record<ItemType, string> = {
  project: 'Project',
  epic: 'Epic',
  feature: 'Feature',
  bug: 'Bug',
  task: 'Task',
  todo: 'Todo',
}

// Project-only health signal. Icons stay monochrome-safe; color carries the
// signal (green/amber/red), mirroring the CLI's health rendering.
export const HEALTH_ICON: Record<ItemHealth, IconCmp> = {
  'on-track': CircleCheck,
  'at-risk': CircleDot,
  'off-track': CircleSlash,
}

export const HEALTH_COLOR: Record<ItemHealth, string> = {
  'on-track': 'text-emerald-600 dark:text-emerald-400',
  'at-risk': 'text-amber-600 dark:text-amber-400',
  'off-track': 'text-red-600 dark:text-red-400',
}

export const HEALTH_LABEL: Record<ItemHealth, string> = {
  'on-track': 'On track',
  'at-risk': 'At risk',
  'off-track': 'Off track',
}

export const KB_BUCKET_ICON: Record<KbBucket, IconCmp> = {
  feature: Boxes,
  doc: BookOpen,
  adr: Scale,
  log: ScrollText,
  idea: Lightbulb,
  source: Link2,
}
