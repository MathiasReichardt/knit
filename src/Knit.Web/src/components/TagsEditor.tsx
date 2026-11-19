import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { InlineChips } from './inline/InlineChips'
import { patchItemSafe } from '../lib/patchItem'
import { useItemTagVocabulary } from '../lib/useTagVocabulary'
import { useToasts } from '../stores/toasts'
import type { Item } from '../api/types'

interface Props {
  item: Item
}

// Free-form cross-cutting labels. Owns persistence only; the chips + add
// combobox live in InlineChips (22). The server normalizes on write
// (trim · drop-empty · case-insensitive dedupe), so a case-only or duplicate
// add is a harmless no-op after the round-trip.
export function TagsEditor({ item }: Props) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [busy, setBusy] = useState(false)
  const vocabulary = useItemTagVocabulary()

  const save = async (next: string[]) => {
    setBusy(true)
    const res = await patchItemSafe(qc, item.id, { tags: next })
    setBusy(false)
    if (!res.ok)
      toast('error', `Tags change failed: ${res.error?.problem?.detail ?? res.error?.message ?? ''}`)
  }

  return (
    <InlineChips
      values={item.tags}
      label="tag"
      suggestions={vocabulary}
      busy={busy}
      onChange={(next) => void save(next)}
    />
  )
}
