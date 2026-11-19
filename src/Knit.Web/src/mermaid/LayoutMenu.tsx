import { useId, useState } from 'react'
import { Menu, RotateCcw } from 'lucide-react'
import type { Scalar } from './frontmatter'
import { optionKey, parseNumberOption, type LayoutOption, type NumberOption } from './layoutOptions'
import { Popover } from './Popover'

export interface LayoutModel {
  options: LayoutOption[]
  /** The fence's frontmatter value per option key: undefined when unset, null when not editable. */
  values: Record<string, Scalar | undefined | null>
  /** Values a legacy `%%{init}%%` directive sets; it wins over the frontmatter. */
  directive: Record<string, Scalar>
  busy: boolean
  /** Writes `value` to the fence's frontmatter, or removes the key when undefined. */
  apply: (option: LayoutOption, value: Scalar | undefined) => void
}

export const CHIP =
  'rounded border px-1.5 py-0.5 border-zinc-200 dark:border-zinc-700 ' +
  'hover:bg-zinc-100 dark:hover:bg-zinc-800 disabled:opacity-50 disabled:hover:bg-transparent'
export const CHIP_ON = 'border-blue-500 bg-blue-50 text-blue-700 dark:border-blue-400 dark:bg-blue-950 dark:text-blue-300'

/** The layout button and its popover. */
export function LayoutMenu({
  layout,
  open,
  onOpenChange,
  buttonClassName,
}: {
  layout: LayoutModel
  open: boolean
  onOpenChange: (open: boolean) => void
  buttonClassName: string
}) {
  return (
    <Popover
      label="Layout options"
      icon={<Menu className="h-3.5 w-3.5" />}
      open={open}
      onOpenChange={onOpenChange}
      buttonClassName={buttonClassName}
      width="w-[28rem]"
    >
      <fieldset disabled={layout.busy} className="grid grid-cols-[auto_1fr_auto] items-center gap-x-2 gap-y-1.5">
        <legend className="mb-1.5 font-semibold">Layout</legend>
        {layout.options.map((o) => (
          <Row key={optionKey(o)} option={o} layout={layout} />
        ))}
      </fieldset>
    </Popover>
  )
}

function Row({ option: o, layout }: { option: LayoutOption; layout: LayoutModel }) {
  const key = optionKey(o)
  const written = layout.values[key]
  const directive = layout.directive[key]
  const locked = directive !== undefined || written === null
  const value = directive ?? written ?? o.default
  const note =
    directive !== undefined
      ? 'Set by the fence’s %%{init}%% directive, which wins over the menu.'
      : written === null
        ? 'The fence’s frontmatter has this in a form the menu doesn’t edit.'
        : undefined
  const id = useId()

  return (
    <>
      <label htmlFor={id} title={o.hint} className="whitespace-nowrap">
        {o.label}
      </label>
      <div title={note}>
        {o.kind === 'choice' && (
          <div
            id={id}
            role="group"
            aria-label={o.label}
            className={o.path[0] === 'direction' ? 'grid grid-cols-2 gap-1' : 'flex flex-wrap gap-1'}
          >
            {[...o.choices, ...(o.choices.includes(String(value)) ? [] : [String(value)])].map((c) => (
              <button
                key={c}
                type="button"
                aria-pressed={c === value}
                disabled={locked}
                className={`${CHIP} ${c === value ? CHIP_ON : ''}`}
                onClick={() => c !== value && layout.apply(o, c)}
              >
                {o.labels?.[c] ?? c}
              </button>
            ))}
          </div>
        )}
        {(o.kind === 'int' || o.kind === 'float') && (
          <NumberInput
            id={id}
            option={o}
            value={typeof written === 'number' || directive !== undefined ? String(value) : ''}
            locked={locked}
            onCommit={(n) => layout.apply(o, n)}
          />
        )}
        {o.kind === 'bool' && (
          <input
            id={id}
            type="checkbox"
            checked={value === true}
            disabled={locked}
            onChange={(e) => layout.apply(o, e.target.checked)}
          />
        )}
        {note && <p className="mt-0.5 text-amber-700 dark:text-amber-400">{note}</p>}
      </div>
      <button
        type="button"
        className={
          'inline-flex h-5 w-5 items-center justify-center rounded text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800 ' +
          // Hidden, not removed: the column keeps its width.
          (written === undefined || written === null || written === o.default ? 'invisible' : '')
        }
        title={`Restore default (${String(o.default)})`}
        aria-label={`Restore default ${o.label}`}
        onClick={() => layout.apply(o, undefined)}
      >
        <RotateCcw className="h-3 w-3" />
      </button>
    </>
  )
}

function NumberInput({
  id,
  option,
  value,
  locked,
  onCommit,
}: {
  id: string
  option: NumberOption
  value: string
  locked: boolean
  onCommit: (n: number) => void
}) {
  const [draft, setDraft] = useState(value)
  const [error, setError] = useState<string | null>(null)
  // A saved edit or an outside change re-seeds the field.
  const [seed, setSeed] = useState(value)
  if (seed !== value) {
    setSeed(value)
    setDraft(value)
    setError(null)
  }

  const commit = () => {
    if (draft === value) return setError(null)
    if (draft.trim() === '' && value === '') return setError(null)
    const r = parseNumberOption(option, draft)
    if ('error' in r) return setError(r.error)
    setError(null)
    onCommit(r.value)
  }

  return (
    <div className="flex items-center gap-1.5">
      <input
        id={id}
        type="text"
        inputMode={option.kind === 'int' ? 'numeric' : 'decimal'}
        value={draft}
        placeholder={String(option.default)}
        disabled={locked}
        aria-invalid={error !== null}
        aria-describedby={error ? `${id}-error` : undefined}
        className={
          'w-16 rounded border bg-transparent px-1 py-0.5 tabular-nums ' +
          (error ? 'border-red-500' : 'border-zinc-200 dark:border-zinc-700')
        }
        onChange={(e) => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === 'Enter') commit()
        }}
      />
      {error && (
        <span id={`${id}-error`} className="text-red-600 dark:text-red-400">
          {error}
        </span>
      )}
    </div>
  )
}
