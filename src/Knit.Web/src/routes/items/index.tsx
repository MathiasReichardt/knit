import { useCallback, useEffect, useRef } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { TreeView } from '../../components/TreeView'
import { ListView } from '../../components/ListView'
import { DetailsPane } from '../../components/DetailsPane'
import { FilterToolbar } from '../../components/FilterToolbar'
import { SummaryStrip } from '../../components/SummaryStrip'
import { ResizableSplit } from '../../components/ResizableSplit'
import { NavigatorUrlSync } from '../../components/NavigatorUrlSync'
import { navigatorSearchSchema } from '../../lib/navigatorSearch'
import { useNavigator, useNavigatorRepo } from '../../stores/navigator'
import { useSelection } from '../../stores/selection'
import { api, qk } from '../../api/client'
import { patchItemSafe } from '../../lib/patchItem'
import { useToasts } from '../../stores/toasts'

export const Route = createFileRoute('/items/')({
  validateSearch: navigatorSearchSchema,
  component: ItemsScreen,
})

function ItemsScreen() {
  const search = Route.useSearch()
  const navigate = useNavigate()
  const selectedId = search.id ?? null
  const view = useNavigator((s) => s.view)
  // Filters are restored per repo once /api/root resolves; rendering the views
  // before that would fetch unfiltered and snap (the treeOpen gotcha).
  const { ready: filtersReady } = useNavigatorRepo()
  const selectedCount = useSelection((s) => s.selected.size)
  const clearSelection = useSelection((s) => s.clear)

  // Selection lives in the URL so browser back/forward moves between
  // previously-viewed items. Each pick pushes a new history entry.
  const setSelectedId = useCallback(
    (id: string | null) => {
      void navigate({
        to: '.',
        search: (prev) => ({ ...prev, id: id ?? undefined }),
        replace: false,
      })
    },
    [navigate],
  )

  // Clear multi-select when the single-selected id changes via SPA navigation
  // (browser back/forward, sidebar click). MUST NOT fire when selectedCount
  // changes — otherwise Ctrl-click immediately wipes the second selection.
  const prevSelectedId = useRef(selectedId)
  useEffect(() => {
    if (prevSelectedId.current !== selectedId) {
      prevSelectedId.current = selectedId
      if (selectedId) clearSelection()
    }
  }, [selectedId, clearSelection])

  // `m` assigns the selected item to me. Scoped to /items because the
  // "current item" only has meaning here (URL `id`). Mirrors the
  // DetailsPane "Assign to me" action.
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const identityQ = useQuery({ queryKey: qk.identity, queryFn: api.identity })
  const me = identityQ.data?.handle ?? null
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return
      if (e.key !== 'm') return
      const t = e.target as HTMLElement | null
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return
      if (!selectedId || !me) return
      e.preventDefault()
      void (async () => {
        const res = await patchItemSafe(qc, selectedId, { assignee: me })
        if (!res.ok) {
          toast('error', `Assign failed: ${res.error?.problem?.detail ?? res.error?.message ?? ''}`)
        } else {
          toast('success', `Assigned to ${me}`)
        }
      })()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selectedId, me, qc, toast])

  return (
    <>
      <NavigatorUrlSync search={search} ready={filtersReady} />
      <ResizableSplit
      storageKey="items"
      left={
        <>
          <FilterToolbar />
          <SummaryStrip />
          {selectedCount > 1 && (
            <div className="flex items-center gap-2 px-3 py-1 text-xs border-b border-blue-200 dark:border-blue-900 bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-200">
              {selectedCount} selected
              <button
                type="button"
                onClick={clearSelection}
                className="ml-auto hover:underline"
              >
                clear
              </button>
            </div>
          )}
          <div className="flex-1 min-h-0">
            {!filtersReady ? null : view === 'tree' ? (
              <TreeView selectedId={selectedId} onSelect={setSelectedId} />
            ) : (
              <ListView selectedId={selectedId} onSelect={setSelectedId} />
            )}
          </div>
        </>
      }
      right={<DetailsPane itemId={selectedId} onSelectItem={setSelectedId} />}
    />
    </>
  )
}
