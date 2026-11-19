import { useEffect, useRef } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { formatVersionWarning } from './formatVersion'
import { useToasts } from '../stores/toasts'

/**
 * Toast once when the repo's on-disk format is newer than this server
 * understands. Warn-only on purpose: the UI stays usable, the CLI is the
 * surface that hard-refuses.
 */
export function useFormatVersionCheck() {
  const { data } = useQuery({ queryKey: ['version'], queryFn: api.version, staleTime: Infinity })
  const push = useToasts((s) => s.push)
  const warned = useRef(false)

  useEffect(() => {
    if (warned.current) return
    const message = formatVersionWarning(data)
    if (!message) return
    warned.current = true
    push('error', message)
  }, [data, push])
}
