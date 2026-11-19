import { allowsGrowToFolder, type KbBucket } from '../api/kb'
import { DROP_NOOP, DROP_OK, type DropVerdict } from './dropVerdict'

/** What is being dragged in the KB tree. */
export interface KbDragPayload {
  ref: string
  bucket: KbBucket
  slug: string
  title: string
}

/** A KB drop zone: a bucket folder (moves to top level), a folder-form entry
 * (becomes its sub-doc), or a flat entry — never a valid parent, but a drop
 * zone so the ghost can say why and how to fix it. */
export type KbDropTarget =
  | { kind: 'bucket'; bucket: KbBucket }
  | { kind: 'entry'; bucket: KbBucket; parentSlug: string }
  | { kind: 'flat'; bucket: KbBucket; slug: string }

function leafOf(slug: string): string {
  return slug.includes('/') ? slug.slice(slug.lastIndexOf('/') + 1) : slug
}

function isSelfOrBelow(slug: string, dragged: string): boolean {
  return slug === dragged || slug.startsWith(dragged + '/')
}

/** The slug the dragged entry ends up with, or null when the drop does not land. */
export function kbDropSlug(payload: KbDragPayload, target: KbDropTarget): string | null {
  if (checkKbDrop(payload, target).kind !== 'ok') return null
  return target.kind === 'entry' ? `${target.parentSlug}/${leafOf(payload.slug)}` : leafOf(payload.slug)
}

export function checkKbDrop(payload: KbDragPayload, target: KbDropTarget): DropVerdict {
  if (target.bucket !== payload.bucket) {
    return {
      kind: 'refused',
      reason: `A ${payload.bucket} entry can't move to ${target.bucket}`,
      hint: `Entries keep their bucket — create a new ${target.bucket} entry instead`,
    }
  }
  switch (target.kind) {
    case 'bucket':
      return payload.slug.includes('/') ? DROP_OK : DROP_NOOP
    case 'entry': {
      if (isSelfOrBelow(target.parentSlug, payload.slug)) return DROP_NOOP
      const already = payload.slug === `${target.parentSlug}/${leafOf(payload.slug)}`
      return already ? DROP_NOOP : DROP_OK
    }
    case 'flat': {
      if (isSelfOrBelow(target.slug, payload.slug)) return DROP_NOOP
      return allowsGrowToFolder(target.bucket)
        ? {
            kind: 'refused',
            reason: `${target.bucket}:${target.slug} can't hold sub-docs yet`,
            hint: 'Split it into folder form first',
          }
        : {
            kind: 'refused',
            reason: `${target.bucket} entries can't hold sub-docs`,
            hint: `Drop it on the ${target.bucket} folder to move it to the top level`,
          }
    }
  }
}
