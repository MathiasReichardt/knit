import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useRouter } from '@tanstack/react-router'
import { CircleCheck, CornerDownLeft, PenLine } from 'lucide-react'
import { api, ApiError, qk } from '../api/client'
import { kbApi, kbQk, type KbBucket } from '../api/kb'
import { designsApi, designsQk } from '../api/designs'
import { classifyRef, isLinkRef } from '../lib/jumpRef'
import { isPastedLink, resolvePastedLink } from '../lib/shareLink'
import { shareApi, shareQk } from '../api/share'
import { followLink } from '../lib/linkCheck'
import { navTarget, resolveLink } from '../lib/resolveLink'
import { jumpMatches, type JumpMatch } from '../lib/jumpMatches'
import { KB_BUCKET_ICON, STATE_COLOR, STATE_ICON, STATE_LABEL, TYPE_COLOR, TYPE_ICON, TYPE_LABEL } from '../lib/glyphs'
import { useDialogs } from '../stores/dialogs'
import { useToasts } from '../stores/toasts'
import { useItemReveal } from '../stores/itemReveal'
import { isNewTabKey, type NavTarget } from '../lib/searchTarget'
import { RowLink } from './RowLink'

// Type or paste a ref and jump. One box, every address space: `bucket:slug`
// resolves to a KB entry, `design:slug` to a design, anything else to an item
// id / short-id prefix. Matches by id, ref or title show as you type; Enter
// takes the highlighted one. With none, the typed ref is confirmed against the
// server (the same probe a markdown link click makes) before navigating — a
// miss toasts instead of routing into a 404.
export function JumpToRef() {
  const open = useDialogs((s) => s.jumpOpen)
  const close = useDialogs((s) => s.closeJump)
  if (!open) return null
  return <JumpBody onClose={close} />
}

const LIST_ID = 'jump-matches'
const optionId = (m: JumpMatch) => `jump-match-${m.key}`

// Pressing an option would blur the input before the click lands.
const keepInputFocus = (e: React.MouseEvent) => e.preventDefault()

function JumpBody({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate()
  const router = useRouter()
  const toast = useToasts((s) => s.push)
  const queryClient = useQueryClient()
  const inputRef = useRef<HTMLInputElement | null>(null)
  const [value, setValue] = useState('')
  const [active, setActive] = useState(0)
  const [busy, setBusy] = useState(false)

  const itemsQ = useQuery({ queryKey: qk.items, queryFn: () => api.items() })
  const kbQ = useQuery({ queryKey: kbQk.list(), queryFn: () => kbApi.list() })
  const designsQ = useQuery({ queryKey: designsQk.list(false), queryFn: () => designsApi.list(false) })
  const completedQ = useQuery({ queryKey: designsQk.list(true), queryFn: () => designsApi.list(true) })
  const matches = useMemo(
    () =>
      jumpMatches(value, {
        items: itemsQ.data ?? [],
        kb: kbQ.data ?? [],
        designs: [...(designsQ.data ?? []), ...(completedQ.data ?? [])],
      }),
    [value, itemsQ.data, kbQ.data, designsQ.data, completedQ.data],
  )
  const highlighted = matches[Math.min(active, matches.length - 1)] ?? null

  useEffect(() => { inputRef.current?.focus() }, [])
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const goKb = (bucket: KbBucket, slug: string) => {
    onClose()
    void navigate(kbTarget(bucket, slug) as never)
  }
  const goDesign = (slug: string, completed: boolean) => {
    onClose()
    void navigate(designTarget(slug, completed) as never)
  }
  const goItem = (id: string) => {
    onClose()
    useItemReveal.getState().request(id)
    void navigate(itemTarget(id) as never)
  }

  const pick = (m: JumpMatch) => {
    if (m.kind === 'item') goItem(m.item.id)
    else if (m.kind === 'kb') goKb(m.entry.bucket, m.entry.slug)
    else goDesign(m.design.slug, m.design.completed)
  }

  // The dialog stays open, so several picks can go to tabs in a row.
  const openInNewTab = (t: NavTarget) =>
    void window.open(router.buildLocation(t as never).href, '_blank', 'noopener')

  // A section ref, or a ref the item / KB / design lookups below don't take
  // (`item:`, `project:`, a `.knit/…` path), goes the way a body link does.
  const followRef = (newTab: boolean) => {
    const ref = value.trim()
    const t = resolveLink('', ref)
    if (newTab) {
      const target = navTarget(t)
      if (target) openInNewTab(target as NavTarget)
      return
    }
    onClose()
    void followLink(t, ref, { navigate: (to) => navigate(to as never), toast })
  }

  // A pasted forge URL or `<repo_name>:<path>`: its `.knit/` path
  // goes the way a typed one does, once it is known to be this repo's.
  const followPasted = async (newTab: boolean) => {
    let origin
    try {
      origin = await queryClient.fetchQuery({ queryKey: shareQk.origin, queryFn: shareApi.origin })
    } catch (e) {
      toast('error', `Lookup failed: ${e instanceof Error ? e.message : String(e)}`)
      return
    }
    const r = resolvePastedLink(value, origin)
    if (!r.ok) {
      toast('error', r.error)
      return
    }
    const t = resolveLink('', r.path)
    if (newTab) {
      const target = navTarget(t)
      if (target) openInNewTab(target as NavTarget)
      return
    }
    onClose()
    void followLink(t, r.path, { navigate: (to) => navigate(to as never), toast })
  }

  const resolve = async (newTab: boolean) => {
    if (isPastedLink(value)) {
      await followPasted(newTab)
      return
    }
    if (isLinkRef(value)) {
      followRef(newTab)
      return
    }
    const target = classifyRef(value)
    if (!target || busy) return
    setBusy(true)
    try {
      if (target.kind === 'kb') {
        await kbApi.entry(target.bucket, target.slug)
        if (newTab) openInNewTab(kbTarget(target.bucket, target.slug))
        else goKb(target.bucket, target.slug)
      } else if (target.kind === 'design') {
        const doc = await designsApi.get(target.slug)
        if (newTab) openInNewTab(designTarget(target.slug, doc.completed))
        else goDesign(target.slug, doc.completed)
      } else {
        // Server resolves a short-id prefix to the full item; navigate by
        // its canonical id so the route doesn't re-resolve the prefix.
        const item = await api.item(target.id)
        if (newTab) openInNewTab(itemTarget(item.id))
        else goItem(item.id)
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) toast('error', `Ref not found: ${value.trim()}`)
      else toast('error', `Lookup failed: ${e instanceof Error ? e.message : String(e)}`)
    } finally {
      setBusy(false)
    }
  }

  const submit = () => {
    if (highlighted && !isLinkRef(value) && !isPastedLink(value)) pick(highlighted)
    else void resolve(false)
  }

  const onInputKey = (e: React.KeyboardEvent) => {
    if (isNewTabKey(e)) {
      e.preventDefault()
      if (highlighted && !isLinkRef(value) && !isPastedLink(value)) openInNewTab(matchTarget(highlighted))
      else void resolve(true)
      return
    }
    if (matches.length === 0) return
    const at = Math.min(active, matches.length - 1)
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((at + 1) % matches.length)
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((at - 1 + matches.length) % matches.length)
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/40 pt-[15vh]"
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}
    >
      <div
        role="dialog"
        aria-modal
        aria-label="Jump to id or ref"
        className="w-[36rem] max-w-[95vw] rounded-md bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 shadow-xl"
      >
        <form
          onSubmit={(e) => { e.preventDefault(); submit() }}
          className="flex items-center gap-2 px-3 py-2"
        >
          <input
            ref={inputRef}
            value={value}
            onChange={(e) => { setValue(e.target.value); setActive(0) }}
            onKeyDown={onInputKey}
            placeholder="Jump to id, ref, title or pasted link — a3f9, adr:0002-foo, design:web-ui"
            aria-label="Ref, item id or title"
            role="combobox"
            aria-expanded={matches.length > 0}
            aria-controls={LIST_ID}
            aria-autocomplete="list"
            aria-activedescendant={highlighted ? optionId(highlighted) : undefined}
            className="flex-1 h-9 bg-transparent text-sm text-zinc-800 dark:text-zinc-200 placeholder:text-zinc-400 outline-none"
          />
          <button
            type="submit"
            disabled={busy || classifyRef(value) === null}
            title="Jump"
            aria-label="Jump"
            className="inline-flex items-center gap-1 h-8 rounded-md bg-blue-600 px-3 text-xs font-medium text-white disabled:opacity-50"
          >
            <CornerDownLeft className="h-3.5 w-3.5" aria-hidden />
            Jump
          </button>
        </form>
        {matches.length > 0 && (
          <ul
            id={LIST_ID}
            role="listbox"
            aria-label="Matches"
            onMouseDown={keepInputFocus}
            className="max-h-80 overflow-auto border-t border-zinc-200 dark:border-zinc-800 py-1"
          >
            {matches.map((m, i) => (
              <MatchRow
                key={m.key}
                match={m}
                active={m === highlighted}
                onPick={() => pick(m)}
                onHover={() => setActive(i)}
              />
            ))}
          </ul>
        )}
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 px-3 py-2 text-[11px] text-zinc-500 border-t border-zinc-200 dark:border-zinc-800">
          <dt className="text-zinc-400">Find</dt>
          <dd>a title, an item id or prefix, a ref</dd>
          <dt className="text-zinc-400">Narrow</dt>
          <dd>
            start with <Code>adr:</Code> <Code>doc:</Code> <Code>feature:</Code> <Code>design:</Code> …
          </dd>
          <dt className="text-zinc-400">Keys</dt>
          <dd>
            <Code>↑</Code> <Code>↓</Code> pick · <Code>Enter</Code> jump · <Code>Ctrl+Enter</Code> new tab ·{' '}
            <Code>Esc</Code> close
          </dd>
        </dl>
      </div>
    </div>
  )
}

function Code({ children }: { children: React.ReactNode }) {
  return <kbd className="font-mono text-zinc-600 dark:text-zinc-300">{children}</kbd>
}

function MatchRow({
  match,
  active,
  onPick,
  onHover,
}: {
  match: JumpMatch
  active: boolean
  onPick: () => void
  onHover: () => void
}) {
  const ref = useRef<HTMLLIElement | null>(null)
  useEffect(() => {
    if (active) ref.current?.scrollIntoView({ block: 'nearest' })
  }, [active])
  return (
    <li
      ref={ref}
      id={optionId(match)}
      role="option"
      aria-selected={active}
      onMouseMove={onHover}
      className={
        active
          ? 'bg-blue-50 text-blue-800 dark:bg-blue-500/15 dark:text-blue-200'
          : 'text-zinc-800 dark:text-zinc-200'
      }
    >
      <RowLink
        {...matchTarget(match)}
        onSelect={onPick}
        tabIndex={-1}
        className="flex items-center gap-2 px-3 py-1.5 text-sm cursor-pointer"
      >
        <MatchGlyphs match={match} />
        <span className="flex-1 truncate">{matchTitle(match)}</span>
        <span className="max-w-[40%] shrink-0 truncate font-mono text-[11px] text-zinc-500">{matchRef(match)}</span>
      </RowLink>
    </li>
  )
}

// Two fixed slots, kind then state, so titles line up down the list. KB
// entries have no state; a design's is only whether it is completed.
function MatchGlyphs({ match }: { match: JumpMatch }) {
  const slot = 'h-4 w-4 shrink-0'
  if (match.kind === 'item') {
    const { type, state } = match.item
    const TypeIcon = TYPE_ICON[type]
    const StateIcon = STATE_ICON[state]
    return (
      <>
        <TypeIcon aria-label={TYPE_LABEL[type]} className={`${TYPE_COLOR[type]} ${slot}`} />
        <StateIcon aria-label={STATE_LABEL[state]} className={`${STATE_COLOR[state]} ${slot}`} />
      </>
    )
  }
  if (match.kind === 'kb') {
    const Icon = KB_BUCKET_ICON[match.entry.bucket]
    return (
      <>
        <Icon aria-label={`KB ${match.entry.bucket}`} className={`text-blue-600 dark:text-blue-400 ${slot}`} />
        <span className={slot} />
      </>
    )
  }
  return (
    <>
      <PenLine aria-label="Design" className={`text-zinc-500 ${slot}`} />
      {match.design.completed ? (
        <CircleCheck aria-label="Completed" className={`${STATE_COLOR.done} ${slot}`} />
      ) : (
        <span className={slot} />
      )}
    </>
  )
}

const itemTarget = (id: string): NavTarget => ({ to: '/items', search: { id } })
const kbTarget = (bucket: KbBucket, slug: string): NavTarget => ({ to: '/kb', search: { bucket, slug } })
// `completed` picks the tab, so the tree can reveal the design.
const designTarget = (slug: string, completed: boolean): NavTarget => ({
  to: '/designs',
  search: { tab: completed ? 'completed' : 'active', slug },
})

const matchTarget = (m: JumpMatch): NavTarget =>
  m.kind === 'item'
    ? itemTarget(m.item.id)
    : m.kind === 'kb'
      ? kbTarget(m.entry.bucket, m.entry.slug)
      : designTarget(m.design.slug, m.design.completed)

const matchTitle = (m: JumpMatch) =>
  m.kind === 'item' ? m.item.title : m.kind === 'kb' ? m.entry.title : m.design.title

const matchRef = (m: JumpMatch) =>
  m.kind === 'item' ? m.item.shortId : m.kind === 'kb' ? m.entry.ref : `design:${m.design.slug}`
