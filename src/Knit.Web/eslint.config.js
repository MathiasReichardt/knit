import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    // TanStack Router route modules must `export const Route` (a non-component)
    // and keep their screen component local — the fast-refresh rule is
    // permanently unsatisfiable here, so scope it off. main.tsx is the app
    // entry (local SseConnector + bootstrap render side-effects), likewise
    // never HMR-refreshed as a component module. The rule stays active for
    // components/, lib/, stores/.
    files: ['src/routes/**/*.{ts,tsx}', 'src/main.tsx'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
  {
    // Every write goes through apiWrite (src/api/http.ts): the API refuses a
    // write without `Content-Type: application/json`, also a bodyless one.
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/api/http.ts', 'src/**/*.test.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': ['error', {
        selector: "CallExpression[callee.name='fetch'] > ObjectExpression > Property[key.name='method']",
        message: 'Send writes through apiWrite / sendJson in src/api/http.ts — they set the JSON content type the API requires.',
      }],
    },
  },
  {
    // src/mermaid/ stays extractable as a standalone package: it may import
    // mermaid, React (+ react-dom), lucide-react and its own files, never
    // Knit code. Knit state (theme, refs, toasts) comes in as props.
    files: ['src/mermaid/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': ['error', {
        patterns: [{
          regex: '^(?!(mermaid|react|react-dom|lucide-react|vitest|@testing-library)(/|$)|\\./)',
          message: 'src/mermaid/ imports only mermaid, react, react-dom, lucide-react, vitest, @testing-library and ./ siblings — pass Knit state in as props.',
        }],
      }],
    },
  },
])
