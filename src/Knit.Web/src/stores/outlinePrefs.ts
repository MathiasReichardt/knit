import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

interface OutlinePrefs {
  pinned: boolean
  setPinned: (v: boolean) => void
}

export const useOutlinePrefs = create<OutlinePrefs>()(
  persist(
    (set) => ({
      pinned: false,
      setPinned: (v) => set({ pinned: v }),
    }),
    { name: 'knit.outline', storage: createJSONStorage(() => localStorage) },
  ),
)
