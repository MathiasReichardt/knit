import { useEffect, useRef } from 'react'
import { EditorState, type Extension } from '@codemirror/state'
import { EditorView, lineNumbers } from '@codemirror/view'
import {
  bracketMatching,
  defaultHighlightStyle,
  StreamLanguage,
  syntaxHighlighting,
} from '@codemirror/language'
import { markdown } from '@codemirror/lang-markdown'
import { javascript } from '@codemirror/lang-javascript'
import { json } from '@codemirror/lang-json'
import { python } from '@codemirror/lang-python'
import { go } from '@codemirror/lang-go'
import { yaml } from '@codemirror/lang-yaml'
import { xml } from '@codemirror/lang-xml'
import { rust } from '@codemirror/lang-rust'
import { cpp } from '@codemirror/lang-cpp'
import { sql } from '@codemirror/lang-sql'
import { css } from '@codemirror/lang-css'
import { sass } from '@codemirror/lang-sass'
import { less } from '@codemirror/lang-less'
import { csharp, java } from '@codemirror/legacy-modes/mode/clike'
import { properties } from '@codemirror/legacy-modes/mode/properties'
import { toml } from '@codemirror/legacy-modes/mode/toml'
import { shell } from '@codemirror/legacy-modes/mode/shell'
import { powerShell } from '@codemirror/legacy-modes/mode/powershell'
import { dockerFile } from '@codemirror/legacy-modes/mode/dockerfile'
import { diff } from '@codemirror/legacy-modes/mode/diff'
import { useResolvedTheme } from '../stores/theme'

interface Props {
  value: string
  mime: string
}

function languageFor(mime: string): Extension | null {
  switch (mime) {
    case 'text/markdown':
      return markdown()
    case 'text/javascript':
      return javascript({ typescript: false, jsx: false })
    case 'text/typescript':
      return javascript({ typescript: true, jsx: true })
    case 'application/json':
      return json()
    case 'text/x-python':
      return python()
    case 'text/x-go':
      return go()
    case 'text/yaml':
      return yaml()
    case 'application/xml':
      return xml()
    case 'text/x-csharp':
      return StreamLanguage.define(csharp)
    case 'text/x-java-source':
      return StreamLanguage.define(java)
    case 'text/x-ini':
      return StreamLanguage.define(properties)
    case 'text/x-toml':
      return StreamLanguage.define(toml)
    case 'text/rust':
      return rust()
    case 'text/x-c':
    case 'text/x-c++':
      return cpp()
    case 'application/sql':
      return sql()
    case 'text/css':
      return css()
    case 'text/x-scss':
    case 'text/x-sass':
      return sass({ indented: false })
    case 'text/x-less':
      return less()
    case 'text/x-shellscript':
      return StreamLanguage.define(shell)
    case 'text/x-powershell':
      return StreamLanguage.define(powerShell)
    case 'text/x-dockerfile':
      return StreamLanguage.define(dockerFile)
    case 'text/x-diff':
      return StreamLanguage.define(diff)
    default:
      return null
  }
}

export function CodeView({ value, mime }: Props) {
  const hostRef = useRef<HTMLDivElement | null>(null)
  const viewRef = useRef<EditorView | null>(null)
  const themeMode = useResolvedTheme()
  const dark = themeMode === 'dark'

  useEffect(() => {
    if (!hostRef.current) return
    const lang = languageFor(mime)
    const extensions: Extension[] = [
      lineNumbers(),
      bracketMatching(),
      syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
      EditorState.tabSize.of(2),
      EditorView.lineWrapping,
      EditorView.editable.of(false),
      EditorState.readOnly.of(true),
      EditorView.theme(
        {
          '&': { backgroundColor: 'transparent', color: 'inherit' },
          '.cm-content': {
            caretColor: 'transparent',
            fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
            fontSize: '13px',
          },
          '.cm-gutters': {
            backgroundColor: 'transparent',
            borderRight: dark ? '1px solid #27272a' : '1px solid #e4e4e7',
            color: dark ? '#52525b' : '#a1a1aa',
          },
          '.cm-activeLine': { backgroundColor: dark ? '#27272a40' : '#f4f4f580' },
          '.cm-activeLineGutter': { backgroundColor: 'transparent' },
          '.cm-selectionBackground, ::selection': {
            backgroundColor: dark ? '#1e3a5f' : '#bfdbfe',
          },
          '&.cm-focused': { outline: 'none' },
        },
        { dark },
      ),
    ]
    if (lang) extensions.push(lang)
    const state = EditorState.create({ doc: value, extensions })
    const view = new EditorView({ state, parent: hostRef.current })
    viewRef.current = view
    return () => {
      view.destroy()
      viewRef.current = null
    }
  }, [value, mime, dark])

  return (
    <div
      ref={hostRef}
      className="border border-zinc-200 dark:border-zinc-800 rounded bg-white dark:bg-zinc-950 min-h-[16rem]"
    />
  )
}
