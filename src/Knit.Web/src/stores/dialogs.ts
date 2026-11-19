import { create } from 'zustand'
import type { ItemType } from '../api/types'
import type { KbBucket } from '../api/kb'

interface NewItemPrefill {
  parentId?: string | null
  type?: ItemType
  // When set, the dialog offers only these types (e.g. the Projects nav
  // restricts creation to project + todo). Defaults to the full set.
  allowedTypes?: ItemType[]
  // When true, stay on the current view after creating (don't navigate to
  // /items). Used by the inline Projects-nav + buttons.
  stayOnCreate?: boolean
}

interface NewKbPrefill {
  bucket?: KbBucket
  // When set, the dialog locks the bucket and prefills the slug with
  // `<parentSlug>/`. Used by the inline `+` button on folder-form
  // entries in the KB tree.
  parentRef?: { bucket: KbBucket; slug: string }
}

interface NewMilestonePrefill {
  // The project doc folder the milestone is created under. Required — a
  // milestone always belongs to a project.
  project: string
}

interface NewProjectDocPrefill {
  // The project doc folder the free-form doc is created under. Required.
  project: string
}

interface DialogsState {
  newItemOpen: boolean
  newItemPrefill: NewItemPrefill
  openNewItem: (prefill?: NewItemPrefill) => void
  closeNewItem: () => void

  newKbOpen: boolean
  newKbPrefill: NewKbPrefill
  openNewKb: (prefill?: NewKbPrefill) => void
  closeNewKb: () => void

  newMilestoneOpen: boolean
  newMilestonePrefill: NewMilestonePrefill | null
  openNewMilestone: (prefill: NewMilestonePrefill) => void
  closeNewMilestone: () => void

  newProjectDocOpen: boolean
  newProjectDocPrefill: NewProjectDocPrefill | null
  openNewProjectDoc: (prefill: NewProjectDocPrefill) => void
  closeNewProjectDoc: () => void

  jumpOpen: boolean
  openJump: () => void
  closeJump: () => void

  shortcutsOpen: boolean
  openShortcuts: () => void
  closeShortcuts: () => void
  toggleShortcuts: () => void

  manageSlatesOpen: boolean
  openManageSlates: () => void
  closeManageSlates: () => void
}

export const useDialogs = create<DialogsState>((set) => ({
  newItemOpen: false,
  newItemPrefill: {},
  openNewItem: (prefill = {}) => set({ newItemOpen: true, newItemPrefill: prefill }),
  closeNewItem: () => set({ newItemOpen: false, newItemPrefill: {} }),

  newKbOpen: false,
  newKbPrefill: {},
  openNewKb: (prefill = {}) => set({ newKbOpen: true, newKbPrefill: prefill }),
  closeNewKb: () => set({ newKbOpen: false, newKbPrefill: {} }),

  newMilestoneOpen: false,
  newMilestonePrefill: null,
  openNewMilestone: (prefill) => set({ newMilestoneOpen: true, newMilestonePrefill: prefill }),
  closeNewMilestone: () => set({ newMilestoneOpen: false, newMilestonePrefill: null }),

  newProjectDocOpen: false,
  newProjectDocPrefill: null,
  openNewProjectDoc: (prefill) => set({ newProjectDocOpen: true, newProjectDocPrefill: prefill }),
  closeNewProjectDoc: () => set({ newProjectDocOpen: false, newProjectDocPrefill: null }),

  jumpOpen: false,
  openJump: () => set({ jumpOpen: true }),
  closeJump: () => set({ jumpOpen: false }),

  shortcutsOpen: false,
  openShortcuts: () => set({ shortcutsOpen: true }),
  closeShortcuts: () => set({ shortcutsOpen: false }),
  toggleShortcuts: () => set((s) => ({ shortcutsOpen: !s.shortcutsOpen })),

  manageSlatesOpen: false,
  openManageSlates: () => set({ manageSlatesOpen: true }),
  closeManageSlates: () => set({ manageSlatesOpen: false }),
}))
