import { createContext, useContext } from 'react'
import { useStore, type StoreApi } from 'zustand'
import type { ItemType, Priority } from '../api/types'
import { useNavigator } from './navigator'

// The filter controls (components/ItemFilterControls) render in both the
// tree/list toolbar and the timeline, each against its own store: the
// navigator by default, the timeline's filters under a Provider.
export interface ItemFilterSlice {
  types: ItemType[]
  toggleType: (t: ItemType) => void
  assignee: string | null
  setAssignee: (a: string | null) => void
  minPriority: Priority | null
  setMinPriority: (p: Priority | null) => void
  tags: string[]
  toggleTag: (t: string) => void
  clearTags: () => void
  slate: string | null
  setSlate: (name: string | null) => void
  noSlate: boolean
  setNoSlate: (v: boolean) => void
  under: string | null
  setUnder: (id: string | null) => void
}

export const ItemFilterStoreContext = createContext<StoreApi<ItemFilterSlice>>(useNavigator)

export function useItemFilters<T>(selector: (s: ItemFilterSlice) => T): T {
  return useStore(useContext(ItemFilterStoreContext), selector)
}

// A caveat the host view appends to every filter's tooltip — the timeline's
// "matches the bar's own item" rule while a depth folds items into bars.
export const FilterHintContext = createContext<string | null>(null)

export function useHinted(): (title: string) => string {
  const hint = useContext(FilterHintContext)
  return (title) => (hint ? `${title}
${hint}` : title)
}
