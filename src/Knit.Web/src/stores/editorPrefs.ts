import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

interface EditorPrefs {
  spellcheck: boolean
  setSpellcheck: (v: boolean) => void
}

export const useEditorPrefs = create<EditorPrefs>()(
  persist(
    (set) => ({
      spellcheck: false,
      setSpellcheck: (v) => set({ spellcheck: v }),
    }),
    { name: 'knit.editor', storage: createJSONStorage(() => localStorage) },
  ),
)
