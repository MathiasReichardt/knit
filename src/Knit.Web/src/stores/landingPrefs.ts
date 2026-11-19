import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

// Landing page: the `me` toggle. Default on — the page is "resume *my*
// work" — and only the Items row honours it (designs/KB have no owner field).
interface LandingPrefsState {
  mine: boolean
  setMine: (v: boolean) => void
}

export const useLandingPrefs = create<LandingPrefsState>()(
  persist(
    (set) => ({
      mine: true,
      setMine: (mine) => set({ mine }),
    }),
    { name: 'knit.landing', storage: createJSONStorage(() => localStorage) },
  ),
)
