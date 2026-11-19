import { useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useQueryClient } from '@tanstack/react-query'
import { Pencil, Trash2 } from 'lucide-react'
import { ApiError, api, qk, type LinkRel } from '../api/client'
import type { Item, LinkRow } from '../api/types'
import { useToasts } from '../stores/toasts'
import { navTarget, resolveLink } from '../lib/resolveLink'
import { followLink } from '../lib/linkCheck'
import { milestoneRefFromPath } from '../lib/milestoneRef'
import { EditLinkDialog } from './EditLinkDialog'
import { RowLink } from './RowLink'

interface Props {
  item: Item
  onSelectItem?: (id: string) => void
}

// Maps the display label (what the body table writes) to the wire `rel`
// the DELETE endpoint expects.
const REL_WIRE: Record<LinkRow['rel'], LinkRel | null> = {
  Parent: 'parent',
  Blocks: 'blocks',
  'Blocked by': 'blocked-by',
  Related: 'related',
  Subtasks: null, // synthesised view; not deletable here
  'KB ref': 'kb-ref',
  Milestone: 'milestone',
  Branch: 'branch',
  Repo: 'repo',
  PR: 'pr',
  Commit: 'commit',
}

// Branch / Repo / PR / Commit point outside the repo: nothing to resolve to a
// file, and the delete key is the target as displayed.
const EXTERNAL: ReadonlySet<LinkRow['rel']> = new Set(['Branch', 'Repo', 'PR', 'Commit'])

export function LinksTable({ item, onSelectItem }: Props) {
  if (!item.links.length) {
    return <p className="text-sm italic text-zinc-500">No links.</p>
  }
  return (
    <table className="w-full text-sm border-collapse">
      <thead>
        <tr className="text-left text-[10px] uppercase tracking-wide text-zinc-500">
          <th className="py-1 pr-3 font-medium">Rel</th>
          <th className="py-1 pr-3 font-medium">Target</th>
          <th className="py-1 pr-3 font-medium">Notes</th>
          <th className="py-1 font-medium w-12" aria-label="actions" />
        </tr>
      </thead>
      <tbody>
        {item.links.map((l, i) => (
          <LinkRowView key={i} item={item} link={l} onSelectItem={onSelectItem} />
        ))}
      </tbody>
    </table>
  )
}

function LinkRowView({
  item,
  link,
  onSelectItem,
}: {
  item: Item
  link: LinkRow
  onSelectItem?: (id: string) => void
}) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const navigate = useNavigate()
  const [confirming, setConfirming] = useState(false)
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)

  const wireRel = REL_WIRE[link.rel]
  // Delete target per rel: KB ref by its ref text, milestone by its qualified
  // <project>/<slug> (derived from the sub-doc path), everything else by item id.
  const external = EXTERNAL.has(link.rel)
  const wireTarget =
    external || wireRel === 'kb-ref'
      ? link.targetText
      : wireRel === 'milestone'
        ? milestoneRefFromPath(link.targetPath)
        : link.targetItemId

  // Non-item targets (KB ref → kb entry / design / file) resolve through the
  // same path the body markdown uses, so the row navigates instead of dead-
  // ending. Item md files live flat in `.knit/items/`.
  const resolved =
    link.targetItemId || external ? null : resolveLink('.knit/items', link.targetPath)
  const target = resolved && navTarget(resolved)
  const onFollow = () => {
    if (!resolved) return
    void followLink(resolved, link.targetPath, {
      navigate: (to) => navigate(to as never),
      toast,
      onItem: onSelectItem,
    })
  }

  const onDelete = async () => {
    if (!wireRel || !wireTarget) return
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.deleteLink(item.id, wireRel, wireTarget, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      qc.invalidateQueries({ queryKey: qk.context(item.id) })
      toast('success', `Removed ${link.rel.toLowerCase()} link`)
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Delete failed'
      toast('error', `Delete failed: ${msg}`)
    } finally {
      setBusy(false)
      setConfirming(false)
    }
  }

  return (
    <tr className="group border-t border-zinc-100 dark:border-zinc-800/70 align-top">
      <td className="py-1 pr-3 whitespace-nowrap text-zinc-700 dark:text-zinc-300">{link.rel}</td>
      <td className="py-1 pr-3">
        {external ? (
          link.targetPath ? (
            <a
              href={link.targetPath}
              target="_blank"
              rel="noreferrer noopener"
              className="text-blue-600 hover:underline dark:text-blue-400"
              title={link.targetPath}
            >
              {link.targetText}
            </a>
          ) : (
            <span className="font-mono text-xs">{link.targetText}</span>
          )
        ) : link.targetItemId && onSelectItem ? (
          <RowLink
            to="/items"
            search={{ id: link.targetItemId }}
            onSelect={() => onSelectItem(link.targetItemId!)}
            className="cursor-pointer text-blue-600 hover:underline dark:text-blue-400"
            title={link.targetPath}
          >
            {link.targetText || link.targetItemId}
          </RowLink>
        ) : target ? (
          <RowLink
            to={target.to}
            search={target.search}
            onSelect={onFollow}
            className="cursor-pointer text-blue-600 hover:underline dark:text-blue-400"
            title={link.targetPath}
          >
            {link.targetText || link.targetPath}
          </RowLink>
        ) : (
          <span title={link.targetPath}>{link.targetText || link.targetPath}</span>
        )}
      </td>
      <td className="py-1 pr-3 text-zinc-600 dark:text-zinc-400">{link.notes || ''}</td>
      <td className="py-1 w-12 text-right whitespace-nowrap">
        {confirming ? (
          <span className="inline-flex items-center gap-1">
            <button
              type="button"
              disabled={busy}
              onClick={() => void onDelete()}
              title="Confirm delete"
              className="text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded bg-red-600 text-white hover:bg-red-700 disabled:opacity-50"
            >
              Del
            </button>
            <button
              type="button"
              onClick={() => setConfirming(false)}
              title="Cancel"
              className="text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded border border-zinc-200 dark:border-zinc-800"
            >
              Cancel
            </button>
          </span>
        ) : (
          wireRel && (
            <span className="inline-flex items-center gap-0.5">
              <button
                type="button"
                onClick={() => setEditing(true)}
                title="Edit link"
                aria-label="Edit link"
                className="opacity-0 group-hover:opacity-100 focus:opacity-100 p-0.5 rounded text-zinc-500 hover:text-blue-600"
              >
                <Pencil className="h-3.5 w-3.5" />
              </button>
              {wireTarget && (
                <button
                  type="button"
                  onClick={() => setConfirming(true)}
                  title="Delete link"
                  aria-label="Delete link"
                  className="opacity-0 group-hover:opacity-100 focus:opacity-100 p-0.5 rounded text-zinc-500 hover:text-red-600"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              )}
            </span>
          )
        )}
        {editing && <EditLinkDialog item={item} link={link} onClose={() => setEditing(false)} />}
      </td>
    </tr>
  )
}
