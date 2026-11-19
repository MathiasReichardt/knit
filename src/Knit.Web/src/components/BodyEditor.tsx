import { useEffect, useMemo, useRef, useState } from 'react'
import { EditorState, StateEffect, StateField } from '@codemirror/state'
import { Decoration, EditorView, keymap, lineNumbers, type DecorationSet } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import {
  bracketMatching,
  defaultHighlightStyle,
  syntaxHighlighting,
} from '@codemirror/language'
import { SpellCheck, X } from 'lucide-react'
import { useEditorPrefs } from '../stores/editorPrefs'
import type { EditPlace } from '../lib/editPlace'
import { useResolvedTheme } from '../stores/theme'
import { extractOutline } from '../lib/outline'
import { headingAtLine, isEditorOutlineKey } from '../lib/outlineNav'
import { OutlinePanel, type OutlineDriver, type OutlineShortcut } from './OutlinePanel'
import { FLASH_MS } from '../lib/headingFlash'

const EDITOR_SHORTCUT: OutlineShortcut = {
  aria: 'Control+Shift+O Alt+O',
  label: 'Ctrl+Shift+O',
  matches: isEditorOutlineKey,
  whileTyping: true,
}
// The outline follows the text this long after the last keystroke.
const OUTLINE_DEBOUNCE_MS = 200
// A heading counts as the one being read once its line is this close to the top.
const ACTIVE_SLACK_PX = 16

// The heading line an outline click landed on pulses, as in the rendered view.
const setFlash = StateEffect.define<number | null>()
const flashLine = Decoration.line({ class: 'knit-flash' })
const flashField = StateField.define<DecorationSet>({
  create: () => Decoration.none,
  update(deco, tr) {
    let next = deco.map(tr.changes)
    for (const e of tr.effects) {
      if (e.is(setFlash)) next = e.value === null ? Decoration.none : Decoration.set([flashLine.range(e.value)])
    }
    return next
  },
  provide: (f) => EditorView.decorations.from(f),
})

interface Props {
  initialValue: string
  /** Open with the cursor here, its line at the place's screen height (else centered). */
  initialPlace?: EditPlace | null
  /** `cursor`: where the view should land after the editor closes. */
  onSave: (value: string, cursor?: EditPlace) => void | Promise<void>
  onCancel: (cursor?: EditPlace) => void
  busy?: boolean
  /**
   * Fill the parent's height (flex column, internal scroll) instead of
   * sizing to content. Use where the editor is the pane's main content
   * (KB / design detail) so there's no wasted space below it. Leave off
   * where the editor sits in a longer flow (item detail, links + comments
   * follow it).
   */
  fill?: boolean
  /** Report the dirty flag up so the session can pick re-seed vs warn. */
  onDirtyChange?: (dirty: boolean) => void
  /**
   * Bump to force a clean re-seed: replace the editor doc with the latest
   * `initialValue` and clear dirty. Used when a passive (clean) editor's doc
   * changed underneath it.
   */
  reseedToken?: number
  /** Show the doc's h2/h3 outline beside the text, from `minHeadings` on (1); `docRef` prefixes the copied section links. */
  outline?: { docRef: string; minHeadings?: number }
}

export function BodyEditor({ initialValue, initialPlace, onSave, onCancel, busy, fill, onDirtyChange, reseedToken, outline }: Props) {
  const rootRef = useRef<HTMLDivElement | null>(null)
  const hostRef = useRef<HTMLDivElement | null>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [view, setView] = useState<EditorView | null>(null)
  const [outlineText, setOutlineText] = useState(initialValue)
  const withOutline = outline !== undefined
  const outlineItems = useMemo(() => (withOutline ? extractOutline(outlineText) : []), [withOutline, outlineText])
  const outlineTimer = useRef<number | undefined>(undefined)
  const driver = useMemo(() => (view ? editorDriver(view) : null), [view])
  const [dirty, setDirty] = useState(false)
  const [confirmExit, setConfirmExit] = useState(false)
  const spellcheck = useEditorPrefs((s) => s.spellcheck)
  const setSpellcheck = useEditorPrefs((s) => s.setSpellcheck)
  const themeMode = useResolvedTheme()
  const dark = themeMode === 'dark'

  // Latest seed value + dirty callback, readable from effects without
  // re-subscribing. Updated in effects (writing a ref during render is
  // disallowed); declared before the reseed effect so the value is current
  // when a reseed fires in the same render.
  const initialValueRef = useRef(initialValue)
  const onDirtyChangeRef = useRef(onDirtyChange)
  useEffect(() => {
    initialValueRef.current = initialValue
    onDirtyChangeRef.current = onDirtyChange
  })

  // Build the view once on mount; spellcheck/theme reconfigure separately.
  useEffect(() => {
    if (!hostRef.current) return
    const state = EditorState.create({
      doc: initialValue,
      extensions: [
        lineNumbers(),
        history(),
        bracketMatching(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        markdown(),
        flashField,
        EditorState.tabSize.of(2),
        EditorView.lineWrapping,
        keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
        EditorView.updateListener.of((u) => {
          if (!u.docChanged) return
          setDirty(true)
          window.clearTimeout(outlineTimer.current)
          outlineTimer.current = window.setTimeout(() => setOutlineText(u.state.doc.toString()), OUTLINE_DEBOUNCE_MS)
        }),
        EditorView.contentAttributes.of({ spellcheck: spellcheck ? 'true' : 'false' }),
        EditorView.theme(
          {
            // Bound the editor so CodeMirror owns the scroll: without this it
            // grows with the document and the surrounding pane scrolls, which
            // CM can't drive — so the caret disappears below the fold while
            // typing. A bounded height makes `.cm-scroller` scroll and CM's
            // native cursor-following keeps the caret in view.
            '&': { backgroundColor: 'transparent', color: 'inherit', ...(fill ? { height: '100%' } : { maxHeight: '65vh' }) },
            '.cm-scroller': { overflow: 'auto' },
            // The editable area spans the whole box, not just the text: a click
            // below a short body lands in it and puts the caret on the last line.
            '.cm-content, .cm-gutter': { minHeight: fill ? '100%' : '16rem', boxSizing: 'border-box' },
            '.cm-content': { caretColor: dark ? '#fafafa' : '#18181b', fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', fontSize: '13px' },
            '.cm-gutters': {
              backgroundColor: 'transparent',
              borderRight: dark ? '1px solid #27272a' : '1px solid #e4e4e7',
              color: dark ? '#52525b' : '#a1a1aa',
            },
            '.cm-activeLine': { backgroundColor: dark ? '#27272a40' : '#f4f4f580' },
            '.cm-activeLineGutter': { backgroundColor: 'transparent' },
            '.cm-selectionBackground, ::selection': { backgroundColor: dark ? '#1e3a5f' : '#bfdbfe' },
            '&.cm-focused': { outline: 'none' },
          },
          { dark },
        ),
      ],
    })
    const view = new EditorView({ state, parent: hostRef.current })
    viewRef.current = view
    setView(view)
    view.focus()
    if (initialPlace) {
      // The item pane scrolls around the editor: bring it into view first.
      hostRef.current.scrollIntoView({ block: 'nearest' })
      openAt(view, initialPlace)
    }
    return () => {
      window.clearTimeout(outlineTimer.current)
      view.destroy()
      viewRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Reconfigure spellcheck without rebuilding the editor.
  useEffect(() => {
    const view = viewRef.current
    if (!view) return
    view.contentDOM.setAttribute('spellcheck', spellcheck ? 'true' : 'false')
  }, [spellcheck])

  // Surface dirty changes to the session (re-seed vs warn decision).
  useEffect(() => {
    onDirtyChangeRef.current?.(dirty)
  }, [dirty])

  // Clean re-seed: when the session bumps the token, replace the doc with the
  // latest seed and drop dirty. Skipped on mount (the doc is already seeded).
  const seededOnce = useRef(false)
  useEffect(() => {
    if (!seededOnce.current) {
      seededOnce.current = true
      return
    }
    const view = viewRef.current
    if (!view) return
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: initialValueRef.current },
    })
    setDirty(false)
  }, [reseedToken])

  const handleSave = async () => {
    const view = viewRef.current
    if (!view) return
    // A successful save unmounts this editor (the session leaves edit mode),
    // so there's no clean state left to track. If the editor survives the
    // save it's because of a conflict (412) — keep `dirty` true so the
    // outside-click guard still protects the unsaved edits.
    await onSave(view.state.doc.toString(), cursorPlace(view))
  }

  const handleKeyDown = async (e: React.KeyboardEvent) => {
    if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 's') {
      e.preventDefault()
      await handleSave()
    } else if (e.key === 'Escape') {
      e.preventDefault()
      onCancel(cursorPlace(viewRef.current))
    }
  }

  // Exit when a pointer-down lands outside the editor (e.g. clicking the
  // frontmatter title). relatedTarget-based blur detection is unreliable here
  // because the title's pen affordance isn't focusable at blur time, so we
  // detect the outside click directly in the capture phase. Unsaved work is
  // guarded with a prompt, and the click is swallowed so the title doesn't
  // enter edit mode behind the modal.
  useEffect(() => {
    const onPointerDown = (e: MouseEvent) => {
      const root = rootRef.current
      if (!root || root.contains(e.target as Node)) return
      // A blocking modal (e.g. the save-conflict dialog) renders outside the
      // editor root. Its backdrop click must not cancel the editor behind it —
      // and since this is a document capture-phase listener, the dialog can't
      // stop the event from reaching us, so we bail on any click inside a modal.
      if ((e.target as Element)?.closest?.('[role="dialog"]')) return
      if (confirmExit) return
      if (dirty) {
        e.preventDefault()
        e.stopPropagation()
        setConfirmExit(true)
      } else {
        onCancel(cursorPlace(viewRef.current))
      }
    }
    document.addEventListener('mousedown', onPointerDown, true)
    return () => document.removeEventListener('mousedown', onPointerDown, true)
  }, [dirty, confirmExit, onCancel])

  return (
    <div
      ref={rootRef}
      className={fill ? 'flex flex-col h-full min-h-0 gap-2' : 'space-y-2'}
      onKeyDown={(e) => void handleKeyDown(e)}
    >
      {confirmExit && (
        <div className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4">
          <div className="w-full max-w-sm rounded-md border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 p-4 space-y-3">
            <h3 className="text-sm font-semibold text-zinc-800 dark:text-zinc-100">
              Exit md editing without saving?
            </h3>
            <p className="text-xs text-zinc-600 dark:text-zinc-300">
              You have unsaved changes. Discard them and leave the editor?
            </p>
            <div className="flex justify-end gap-2 pt-1">
              <button
                type="button"
                onClick={() => { setConfirmExit(false); viewRef.current?.focus() }}
                className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800 hover:bg-zinc-100 dark:hover:bg-zinc-900"
              >
                Keep editing
              </button>
              <button
                type="button"
                onClick={() => { setConfirmExit(false); onCancel(cursorPlace(viewRef.current)) }}
                className="text-xs px-2 py-1 rounded bg-red-600 hover:bg-red-700 text-white"
              >
                Discard
              </button>
            </div>
          </div>
        </div>
      )}
      <div className="flex items-center justify-between gap-2 text-xs">
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setSpellcheck(!spellcheck)}
            title={spellcheck ? 'Spellcheck on' : 'Spellcheck off'}
            aria-label="Toggle spellcheck"
            aria-pressed={spellcheck}
            className={
              'inline-flex items-center gap-1 px-2 py-1 rounded border text-xs ' +
              (spellcheck
                ? 'border-blue-400 text-blue-700 dark:text-blue-300 bg-blue-50 dark:bg-blue-950/40'
                : 'border-zinc-200 dark:border-zinc-800 text-zinc-600 dark:text-zinc-400 hover:bg-zinc-100 dark:hover:bg-zinc-900')
            }
          >
            <SpellCheck className="h-3.5 w-3.5" aria-hidden />
            Spellcheck
          </button>
          {dirty && <span className="text-amber-600 dark:text-amber-400">unsaved</span>}
        </div>
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            onClick={() => onCancel(cursorPlace(viewRef.current))}
            className="inline-flex items-center gap-1 px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800 hover:bg-zinc-100 dark:hover:bg-zinc-900"
          >
            <X className="h-3.5 w-3.5" aria-hidden />
            Cancel
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() => void handleSave()}
            className="px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
            title="Save (Ctrl/Cmd-S)"
          >
            {busy ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
      <div className={'flex ' + (fill ? 'flex-1 min-h-0' : '')}>
        <div
          ref={hostRef}
          className="flex-1 min-w-0 min-h-0 border border-zinc-200 dark:border-zinc-800 rounded bg-white dark:bg-zinc-950"
        />
        {outline && driver && outlineItems.length >= (outline.minHeadings ?? 1) && (
          <OutlinePanel items={outlineItems} docRef={outline.docRef} driver={driver} shortcut={EDITOR_SHORTCUT} reserve={8} />
        )}
      </div>
    </div>
  )
}

function editorDriver(view: EditorView): OutlineDriver {
  const outline = () => extractOutline(view.state.doc.toString())
  let flashTimer: number | undefined
  const flash = (pos: number | null) => {
    if (view.dom.isConnected) view.dispatch({ effects: setFlash.of(pos) })
  }
  const topLine = (slack: number) => {
    const top = view.scrollDOM.getBoundingClientRect().top + slack
    return view.state.doc.lineAt(view.lineBlockAtHeight(top - view.documentTop).from).number
  }
  return {
    scroller: () => view.scrollDOM,
    watch(onScroll) {
      view.scrollDOM.addEventListener('scroll', onScroll, { passive: true })
      return () => view.scrollDOM.removeEventListener('scroll', onScroll)
    },
    activeId: () => headingAtLine(outline(), topLine(ACTIVE_SLACK_PX)),
    jump(id, withFlash) {
      // The outline lags the text by the debounce: find the heading in the text as it is now.
      const line = outline().find((i) => i.id === id)?.line
      if (!line) return
      const pos = view.state.doc.line(line).from
      view.dispatch({ selection: { anchor: pos }, effects: [EditorView.scrollIntoView(pos, { y: 'start' }), setFlash.of(null)] })
      window.clearTimeout(flashTimer)
      if (!withFlash) return
      // A frame without the class first, so a second click on the same heading pulses again.
      requestAnimationFrame(() => flash(pos))
      flashTimer = window.setTimeout(() => flash(null), FLASH_MS)
    },
    anchor() {
      const snapshot = view.scrollSnapshot()
      return () => view.dispatch({ effects: snapshot })
    },
    home: () => view.focus(),
  }
}

function openAt(view: EditorView, place: EditPlace) {
  const doc = view.state.doc
  const line = doc.line(Math.min(Math.max(place.line, 1), doc.lines))
  const pos = Math.min(line.from + (place.column ?? 0), line.to)
  const box = view.scrollDOM.getBoundingClientRect()
  const margin = place.top - box.top
  const fits = margin >= 0 && margin <= box.height - 2 * view.defaultLineHeight
  view.dispatch({
    selection: { anchor: pos },
    effects: EditorView.scrollIntoView(pos, fits ? { y: 'start', yMargin: margin } : { y: 'center' }),
  })
}

// The cursor's line and screen height; a cursor scrolled out of sight gives
// the top visible line instead, so the view lands on what was on screen.
function cursorPlace(view: EditorView | null): EditPlace | undefined {
  if (!view) return undefined
  const box = view.scrollDOM.getBoundingClientRect()
  const head = view.state.selection.main.head
  const at = view.coordsAtPos(head)
  if (at && at.top >= box.top && at.bottom <= box.bottom) return { line: view.state.doc.lineAt(head).number, top: at.top }
  const top = Math.max(box.top, 0)
  const block = view.lineBlockAtHeight(top - view.documentTop)
  return { line: view.state.doc.lineAt(block.from).number, top: view.documentTop + block.top }
}
