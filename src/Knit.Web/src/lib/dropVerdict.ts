/**
 * What releasing a drag over one drop zone would do. Computed while hovering,
 * so the ghost and the zone can say "no" before the user lets go — a release
 * on anything but `ok` does nothing.
 *
 * `noop` is an accidental drop (onto itself, or where it already is): no
 * explanation, just no effect. `refused` is a rule the user can act on.
 */
export type DropVerdict =
  | { kind: 'ok' }
  | { kind: 'noop' }
  | { kind: 'refused'; reason: string; hint?: string }

export const DROP_OK: DropVerdict = { kind: 'ok' }
export const DROP_NOOP: DropVerdict = { kind: 'noop' }
