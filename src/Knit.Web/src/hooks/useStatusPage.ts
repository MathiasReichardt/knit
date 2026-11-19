import { useCallback, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { reportToast, statusPageApi, statusPageQk, statusReportApi } from '../api/status'
import { useToasts } from '../stores/toasts'

/**
 * The generated status page's state. Polled: the page lives outside .knit/, so
 * no item event reports a run from the terminal or a deleted file.
 */
export function useStatusPage() {
  return useQuery({ queryKey: statusPageQk.get, queryFn: statusPageApi.get, refetchInterval: 60_000 })
}

/** The menu's and the view's "write it now": the toast, and a refetch so the dot and the view follow. */
export function useWriteStatusReport() {
  const [writing, setWriting] = useState(false)
  const push = useToasts((s) => s.push)
  const qc = useQueryClient()
  const write = useCallback(async () => {
    setWriting(true)
    let toast
    try { toast = reportToast(await statusReportApi.write()) }
    catch (e) { toast = reportToast(e) }
    finally { setWriting(false) }
    push(toast.kind, toast.message, { command: toast.command })
    await qc.invalidateQueries({ queryKey: statusPageQk.get })
    await qc.invalidateQueries({ queryKey: ['file', 'view'] })
  }, [push, qc])
  return { write, writing }
}
