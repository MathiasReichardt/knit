import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { InlineChips } from './inline/InlineChips'
import { patchItemSafe } from '../lib/patchItem'
import { useToasts } from '../stores/toasts'
import type { Item } from '../api/types'

interface Props {
  item: Item
}

// Skills an agent should load for this item. Same chips surface as tags (22),
// but with no autocomplete: /api/items summaries don't carry agent_skills, and
// widening the tree payload to feed a suggestion list isn't worth it.
export function AgentSkillsEditor({ item }: Props) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [busy, setBusy] = useState(false)

  const save = async (next: string[]) => {
    setBusy(true)
    const res = await patchItemSafe(qc, item.id, { agentSkills: next })
    setBusy(false)
    if (!res.ok)
      toast('error', `Skills change failed: ${res.error?.problem?.detail ?? res.error?.message ?? ''}`)
  }

  return (
    <InlineChips
      values={item.agentSkills}
      label="skill"
      busy={busy}
      onChange={(next) => void save(next)}
    />
  )
}
