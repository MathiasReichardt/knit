import type { ItemState } from '../api/types'

export interface StateActions {
  approve: boolean
  /** The state allows Start; `start` also needs someone to assign it to. */
  startable: boolean
  /** Start from review sends the item back to in-progress, findings in a comment. */
  sendBack: boolean
  submit: boolean
  done: boolean
  reopen: boolean
  dismiss: boolean
}

/**
 * The state buttons the detail view offers, mirroring the CLI verbs. Start on
 * a draft promotes it (the CLI asks a human the same question); Done doesn't:
 * closing work nobody specified is refused in both surfaces.
 */
export function stateActions(state: ItemState): StateActions {
  const closed = state === 'done' || state === 'dismissed'
  const draft = state === 'draft'
  return {
    approve: draft,
    startable: state !== 'in-progress' && !closed,
    sendBack: state === 'review',
    submit: state === 'in-progress',
    done: !closed && !draft,
    reopen: state !== 'todo' && !draft,
    dismiss: !closed,
  }
}
