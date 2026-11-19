import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { ArrowUp, Compass, FileText, ListChecks } from 'lucide-react'
import { api, qk } from '../api/client'
import { CollapsibleSection } from './CollapsibleSection'
import { RowLink } from './RowLink'
import { STATE_COLOR, STATE_ICON } from '../lib/glyphs'
import { navTarget, resolveLink } from '../lib/resolveLink'
import { followLink } from '../lib/linkCheck'
import { useToasts } from '../stores/toasts'

interface Props {
  itemId: string
  onSelectItem: (id: string) => void
}

export function ContextPanel({ itemId, onSelectItem }: Props) {
  const q = useQuery({
    queryKey: qk.context(itemId),
    queryFn: () => api.itemContext(itemId),
  })
  const navigate = useNavigate()
  const toast = useToasts((s) => s.push)

  // Hide the entire control when there's nothing to show. Loading and
  // error states still render so the user knows fetching failed.
  if (q.data) {
    const d = q.data
    const hasAny =
      d.blocked.length > 0 ||
      !!d.design ||
      d.parentChain.length > 0 ||
      d.agentSkills.length > 0 ||
      d.acceptance.length > 0 ||
      d.codePointers.length > 0 ||
      d.blockers.length > 0 ||
      d.kbRefs.length > 0
    if (!hasAny) return null
  }

  return (
    <CollapsibleSection icon={Compass} label="Context" persistKey="details.context">
      <div className="space-y-3">
          {q.isLoading && <p className="text-sm text-zinc-500 pt-2">Loading…</p>}
          {q.error && <p className="text-sm text-red-600 pt-2">Failed to load context</p>}
          {q.data && (
            <>
              {q.data.blocked.map((b) => (
                <div
                  key={b.inheritedFrom ?? ''}
                  className="text-sm bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900 rounded px-2 py-1.5 text-red-700 dark:text-red-300"
                >
                  <strong className="font-semibold">
                    {b.inheritedFrom ? (
                      <>
                        Blocked by parent{' '}
                        <RowLink
                          to="/items"
                          search={{ id: b.inheritedFrom }}
                          onSelect={() => onSelectItem(b.inheritedFrom!)}
                          className="cursor-pointer hover:underline"
                        >
                          {q.data.parentChain.find((p) => p.id === b.inheritedFrom)?.title ?? b.inheritedFrom}
                        </RowLink>
                        :
                      </>
                    ) : (
                      'Blocked:'
                    )}
                  </strong>{' '}
                  {b.note}
                  {b.blockedBy.map((r, i) => (
                    <span key={i}>
                      {(b.note || i > 0) && '; '}
                      blocked by{' '}
                      {r.id ? (
                        <RowLink
                          to="/items"
                          search={{ id: r.id }}
                          onSelect={() => onSelectItem(r.id!)}
                          className="cursor-pointer hover:underline"
                        >
                          {r.title}
                        </RowLink>
                      ) : (
                        r.title
                      )}
                      {r.notes && ` — ${r.notes}`}
                    </span>
                  ))}
                </div>
              ))}
              <Breadcrumb parentChain={q.data.parentChain} onSelectItem={onSelectItem} />
              {q.data.design && (
                <Section title="Design" icon={FileText}>
                  <div className="flex items-center gap-2 text-sm">
                    <Link
                      to="/designs"
                      search={{ slug: q.data.design.slug }}
                      className="text-blue-600 hover:underline dark:text-blue-400 font-mono text-xs truncate"
                    >
                      design:{q.data.design.slug}
                    </Link>
                    {q.data.design.inheritedFrom && (
                      <span className="text-[11px] text-zinc-500 italic">inherited</span>
                    )}
                  </div>
                </Section>
              )}
              {q.data.agentSkills.length > 0 && (
                <Section title="Agent skills">
                  <div className="flex flex-wrap gap-1.5">
                    {q.data.agentSkills.map((s) => (
                      <span
                        key={s.skill}
                        title={s.inheritedFrom ? `inherited from ${s.inheritedFrom}` : 'own'}
                        className={
                          'inline-flex items-center gap-1 text-[11px] rounded-full border px-2 py-0.5 font-mono ' +
                          (s.inheritedFrom
                            ? 'bg-zinc-50 dark:bg-zinc-900/40 border-zinc-200 dark:border-zinc-800 text-zinc-600 dark:text-zinc-400'
                            : 'bg-zinc-100 dark:bg-zinc-800 border-zinc-200 dark:border-zinc-700')
                        }
                      >
                        {s.inheritedFrom && (
                          <ArrowUp
                            className="h-3 w-3 opacity-60"
                            aria-label="inherited"
                          />
                        )}
                        {s.skill}
                      </span>
                    ))}
                  </div>
                </Section>
              )}
              {q.data.acceptance.length > 0 && (
                <Section title="Acceptance criteria" icon={ListChecks}>
                  <ul className="space-y-1">
                    {q.data.acceptance.map((a, i) => (
                      <li key={i} className="flex items-start gap-2 text-sm">
                        <input
                          type="checkbox"
                          disabled
                          checked={a.checked === true}
                          className="mt-1 cursor-not-allowed"
                          aria-label={`Acceptance: ${a.text}`}
                        />
                        <span className="text-zinc-700 dark:text-zinc-300">{a.text}</span>
                      </li>
                    ))}
                  </ul>
                </Section>
              )}
              {q.data.codePointers.length > 0 && (
                <Section title="Code pointers">
                  <ul className="list-disc pl-5 text-sm text-zinc-700 dark:text-zinc-300 space-y-0.5">
                    {q.data.codePointers.map((p, i) => (
                      <li key={i} className="font-mono text-xs">{p}</li>
                    ))}
                  </ul>
                </Section>
              )}
              {q.data.blockers.length > 0 && (
                <Section title="Blockers">
                  <ul className="space-y-1 text-sm">
                    {q.data.blockers.map((b) => {
                      const StateIcon = STATE_ICON[b.state]
                      return (
                      <li key={b.id} className="flex items-center gap-2">
                        <StateIcon
                          aria-label={b.state}
                          className={
                            STATE_COLOR[b.state] + ' h-4 w-4 shrink-0'
                          }
                        />
                        <RowLink
                          to="/items"
                          search={{ id: b.id }}
                          onSelect={() => onSelectItem(b.id)}
                          className="cursor-pointer text-blue-600 hover:underline dark:text-blue-400 truncate"
                        >
                          {b.title}
                        </RowLink>
                        <span className="font-mono text-[11px] text-zinc-500">{b.shortId}</span>
                      </li>
                      )
                    })}
                  </ul>
                </Section>
              )}
              {q.data.externalRefs.length > 0 && (
                <Section title="External">
                  <ul className="space-y-1 text-sm">
                    {q.data.externalRefs.map((x, i) => (
                      <li key={i} className="text-zinc-700 dark:text-zinc-300">
                        <span className="text-[11px] uppercase tracking-wide text-zinc-500 mr-2">
                          {x.rel}
                        </span>
                        {x.url ? (
                          <a
                            href={x.url}
                            target="_blank"
                            rel="noreferrer noopener"
                            className="text-blue-600 hover:underline dark:text-blue-400"
                          >
                            {x.target}
                          </a>
                        ) : (
                          <span className="font-mono text-xs">{x.target}</span>
                        )}
                        {x.inheritedFrom && (
                          <span className="ml-2 text-[11px] text-zinc-500 italic">inherited</span>
                        )}
                        {x.notes && <span className="ml-2 text-xs text-zinc-500">— {x.notes}</span>}
                      </li>
                    ))}
                  </ul>
                </Section>
              )}
              {q.data.kbRefs.length > 0 && (
                <Section title="KB refs">
                  <ul className="space-y-1 text-sm">
                    {q.data.kbRefs.map((k, i) => {
                      // Item md files live flat in `.knit/items/`; the path is
                      // relative to it, inherited or not.
                      const resolved = resolveLink('.knit/items', k.targetPath)
                      const target = navTarget(resolved)
                      const label = k.targetText || k.targetPath
                      return (
                      <li key={i} className="text-zinc-700 dark:text-zinc-300">
                        {target ? (
                          <RowLink
                            to={target.to}
                            search={target.search}
                            onSelect={() =>
                              void followLink(resolved, k.targetPath, {
                                navigate: (to) => navigate(to as never),
                                toast,
                              })
                            }
                            className="cursor-pointer font-mono text-xs text-blue-600 hover:underline dark:text-blue-400"
                            title={k.targetPath}
                          >
                            {label}
                          </RowLink>
                        ) : (
                          <span className="font-mono text-xs">{label}</span>
                        )}
                        {k.inheritedFrom && (
                          <span className="ml-2 text-[11px] text-zinc-500 italic">inherited</span>
                        )}
                        {k.notes && <span className="ml-2 text-xs text-zinc-500">— {k.notes}</span>}
                      </li>
                      )
                    })}
                  </ul>
                </Section>
              )}
            </>
          )}
      </div>
    </CollapsibleSection>
  )
}

function Section({
  title,
  icon: Icon,
  children,
}: {
  title: string
  icon?: React.ComponentType<{ className?: string; 'aria-hidden'?: boolean }>
  children: React.ReactNode
}) {
  return (
    <div>
      <h3 className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-zinc-700 dark:text-zinc-300 mb-1">
        {Icon && <Icon className="h-3.5 w-3.5" aria-hidden />}
        {title}
      </h3>
      {children}
    </div>
  )
}

function Breadcrumb({
  parentChain,
  onSelectItem,
}: {
  parentChain: { id: string; shortId: string; title: string }[]
  onSelectItem: (id: string) => void
}) {
  if (parentChain.length === 0) return null
  return (
    <nav aria-label="Parent chain" className="flex flex-wrap items-center gap-1 text-xs">
      {[...parentChain].reverse().map((p, i, arr) => (
        <span key={p.id} className="inline-flex items-center gap-1">
          <RowLink
            to="/items"
            search={{ id: p.id }}
            onSelect={() => onSelectItem(p.id)}
            className="cursor-pointer text-blue-600 hover:underline dark:text-blue-400 truncate"
          >
            {p.title}
          </RowLink>
          {i < arr.length - 1 && <span className="text-zinc-400">›</span>}
        </span>
      ))}
    </nav>
  )
}
